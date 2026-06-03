import { test as setup, expect } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";
import { ADMIN_CREDENTIALS, buildStorageState, login } from "./helpers/api";

/**
 * "Spec" de setup: hace login programático contra el backend y persiste el
 * storageState autenticado en disco. Los specs del project `authenticated` lo
 * reutilizan (ver playwright.config.ts) para no pagar el login UI en cada test.
 *
 * Sólo auth.spec.ts ejerce el form real de login.
 */

const AUTH_FILE = path.join(__dirname, ".auth", "user.json");

setup("autenticar (login programático)", async ({ page, request, baseURL }) => {
  const origin = baseURL ?? "http://localhost:3000";

  // 1. Login vía API → tokens + user.
  const auth = await login(request, ADMIN_CREDENTIALS);
  expect(auth.accessToken, "el backend debe devolver un access token").toBeTruthy();
  expect(auth.user, "el login debe incluir el bloque user").not.toBeNull();

  // 2. Sembrar el localStorage del origin del web app. Para escribir en
  //    localStorage hay que estar parados en el origin → navegamos a /login
  //    (público, no redirige) y seteamos las claves que lee lib/auth/storage.ts.
  await page.goto("/login");
  await page.evaluate(
    ({ keys, accessToken, refreshToken, user }) => {
      window.localStorage.setItem(keys.access, accessToken);
      window.localStorage.setItem(keys.refresh, refreshToken);
      window.localStorage.setItem(keys.user, JSON.stringify(user));
    },
    {
      keys: { access: "fc:access", refresh: "fc:refresh", user: "fc:user" },
      accessToken: auth.accessToken,
      refreshToken: auth.refreshToken,
      user: auth.user,
    },
  );

  // 3. Persistir el storageState. Lo escribimos a mano (en vez de
  //    page.context().storageState) para garantizar la forma exacta aun si el
  //    front todavía no consumió las claves.
  fs.mkdirSync(path.dirname(AUTH_FILE), { recursive: true });
  fs.writeFileSync(AUTH_FILE, JSON.stringify(buildStorageState(origin, auth), null, 2));

  // 4. Sanity check: con el storageState la app debe dejarnos entrar a /dashboard.
  await page.goto("/dashboard");
  await expect(page).toHaveURL(/\/dashboard/);
});
