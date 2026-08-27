import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, isAbsolute, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const sdkRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const desktopPluginRoot = join(sdkRoot, "packages", "plugin");
const sdkPackage = readJson(join(sdkRoot, "package.json"));
const desktopPluginPackage = readJson(join(desktopPluginRoot, "package.json"));

assert.equal(sdkPackage.name, "@dotcraft/sdk");
assert.equal(desktopPluginPackage.name, "@dotcraft/plugin");
assert.equal(desktopPluginPackage.version, sdkPackage.version);
assert.equal(desktopPluginPackage.dependencies["@dotcraft/sdk"], sdkPackage.version);

const workRoot = mkdtempSync(join(tmpdir(), "dotcraft-plugin-consumer-"));
const packageRoot = join(workRoot, "packages");
const projectRoot = join(workRoot, "plugin", "desktop");

try {
  mkdirSync(packageRoot, { recursive: true });
  mkdirSync(join(projectRoot, "src"), { recursive: true });

  runNpm(["pack", "--pack-destination", packageRoot], sdkRoot);
  runNpm(["pack", "--pack-destination", packageRoot], desktopPluginRoot);

  const sdkTarball = join(packageRoot, tarballName(sdkPackage.name, sdkPackage.version));
  const desktopPluginTarball = join(
    packageRoot,
    tarballName(desktopPluginPackage.name, desktopPluginPackage.version),
  );
  assert.ok(existsSync(sdkTarball), `Missing packed SDK: ${sdkTarball}`);
  assert.ok(existsSync(desktopPluginTarball), `Missing packed Desktop Plugin SDK: ${desktopPluginTarball}`);

  const packageJson = `${JSON.stringify({
    name: "desktop-plugin-consumer",
    version: "0.1.0",
    private: true,
    type: "module",
    scripts: {
      build: "tsc --noEmit && dotcraft-plugin build",
    },
    devDependencies: {
      "@dotcraft/plugin": sdkPackage.version,
      "@types/react": "^19.0.0",
      "@types/react-dom": "^19.0.0",
      react: "^19.0.0",
      "react-dom": "^19.0.0",
      typescript: "^5.7.0",
    },
  }, null, 2)}\n`;
  writeFileSync(join(projectRoot, "package.json"), packageJson);
  writeFileSync(join(projectRoot, "tsconfig.json"), `${JSON.stringify({
    compilerOptions: {
      module: "ESNext",
      moduleResolution: "Bundler",
      target: "ES2022",
      jsx: "react-jsx",
      strict: true,
      noEmit: true,
    },
    include: ["src/**/*.ts", "src/**/*.tsx"],
  }, null, 2)}\n`);
  writeFileSync(join(projectRoot, "src", "index.tsx"), `import { Button, type DesktopPluginActivate, type DesktopPluginViewProps } from "@dotcraft/plugin";
import "./index.css";

function MainView({ host }: DesktopPluginViewProps) {
  return (
    <main className="plugin-main-view">
      <h1>Desktop Plugin Consumer</h1>
      <Button onClick={() => host.ui.showToast({ message: "Desktop Plugin Consumer is active." })}>
        Verify plugin
      </Button>
    </main>
  );
}

export const activate: DesktopPluginActivate = () => ({
  mainViews: [
    {
      id: "main",
      label: { default: "Desktop Plugin Consumer" },
      order: 80,
      component: MainView,
    },
  ],
});
`);
  writeFileSync(join(projectRoot, "src", "index.css"), `.plugin-main-view {
  color: var(--text-primary);
}
`);

  runNpm([
    "install",
    "--no-save",
    "--package-lock=false",
    "--fund=false",
    "--audit=false",
    sdkTarball,
    desktopPluginTarball,
  ], projectRoot);

  assert.equal(readFileSync(join(projectRoot, "package.json"), "utf8"), packageJson);
  assertInstalledCopy(projectRoot, "@dotcraft/sdk", sdkPackage.version);
  assertInstalledCopy(projectRoot, "@dotcraft/plugin", desktopPluginPackage.version);

  runNpm(["run", "build"], projectRoot);

  const moduleOutput = readFileSync(join(projectRoot, "dist", "index.mjs"), "utf8");
  assert.doesNotMatch(moduleOutput, /["']react(?:-dom)?(?:\/[^"']*)?["']/);
  assert.match(moduleOutput, /dotcraft\.desktop-plugin\.runtime/);
  assert.ok(existsSync(join(projectRoot, "dist", "index.css")));
  console.log(`Verified isolated Desktop Plugin consumer for ${sdkPackage.version}.`);
} finally {
  rmSync(workRoot, { recursive: true, force: true });
}

function runNpm(args, cwd) {
  const npmCli = process.env.npm_execpath
    ?? join(dirname(process.execPath), "node_modules", "npm", "bin", "npm-cli.js");
  const result = spawnSync(process.execPath, [npmCli, ...args], { cwd, encoding: "utf8" });
  if (result.status !== 0) {
    process.stdout.write(result.stdout ?? "");
    process.stderr.write(result.stderr ?? "");
    throw new Error(`npm ${args.join(" ")} failed with exit code ${result.status}.`);
  }
}

function assertInstalledCopy(projectRoot, packageName, expectedVersion) {
  const packagePath = join(projectRoot, "node_modules", ...packageName.split("/"));
  assert.equal(lstatSync(packagePath).isSymbolicLink(), false, `${packageName} must not be a workspace link.`);
  const installedRoot = realpathSync(packagePath);
  const nodeModulesRoot = realpathSync(join(projectRoot, "node_modules"));
  const installedRelative = relative(nodeModulesRoot, installedRoot);
  assert.ok(
    installedRelative && !installedRelative.startsWith("..") && !isAbsolute(installedRelative),
    `${packageName} resolved outside the isolated node_modules directory.`,
  );
  assert.equal(readJson(join(installedRoot, "package.json")).version, expectedVersion);
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function tarballName(packageName, version) {
  return `${packageName.replace(/^@/, "").replace("/", "-")}-${version}.tgz`;
}
