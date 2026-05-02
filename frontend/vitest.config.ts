import { defineConfig } from "vitest/config";
import { resolve } from "path";

export default defineConfig({
  resolve: {
    // Mirror the Nuxt ~ alias (app/) so tests can use the same import paths as
    // source files. Without this, vitest cannot resolve ~/types/game etc.
    alias: {
      "~": resolve(__dirname, "app"),
    },
  },
  // Replicate the Nuxt dev-mode flag so tests can exercise import.meta.dev branches.
  // Without this, import.meta.dev is undefined (falsy) and dev-only code paths are
  // invisible to the test suite — e.g. the schema-validation guard in applyDelta would
  // never be reached.
  //
  // Trade-off: because this is a global define (not a per-test stub), no test in this
  // suite can observe the !import.meta.dev (production) branch. Per-test stubbing was
  // investigated but vi.stubGlobal does not reach import.meta.dev references inside
  // already-imported modules, and the alternatives (vi.stubEnv, setup-file conditional
  // stubs, helper indirection) add complexity that outweighs the value today.
  // Coverage gap: there are currently no prod-gated branches in the codebase that need
  // independent test coverage. A future test that must exercise a prod-only path will
  // need to revisit this approach (e.g. use a dedicated vitest project with its own
  // define override, or extract the prod path into a separately importable helper).
  define: {
    "import.meta.dev": true,
  },
  test: {
    include: ["app/**/*.test.ts", "scripts/**/*.test.ts"],
    // Default environment stays `node` so tests that don't need the DOM keep
    // running under the lighter, faster runtime (and don't pick up happy-dom's
    // console shim, which interferes with `vi.spyOn(console, …)` in the
    // applyDelta dev-mode contract test). Tests that need browser globals
    // (e.g. localStorage in useWebSocket.test.ts) opt in per-file via the
    // `@vitest-environment happy-dom` doc directive at the top of the file.
  },
});
