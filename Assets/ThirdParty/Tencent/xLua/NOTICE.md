# xLua vendor note

- Upstream: `Tencent/xLua`
- Version: `v2.1.16`
- Managed source: official `v2.1.16` tag, runtime root files only
- Native runtime: official `lua54_v2.1.16.tgz`, Windows x86_64 `xlua.dll` only
- Release archive SHA-256: `58c919daf39709ec3dc8ae49a7cb019d7664c80ceb701e6050fa992bd3b3f30a`
- Vendored DLL SHA-256: `a88044f502eb51ec2710c07b107254ec8522e35829f34232893b357669ac97e2`
- License: see `LICENSE.TXT`

The upstream Editor generator, Examples, Hotfix tooling and non-Windows binaries are deliberately excluded. An Assembly Definition Reference compiles the managed runtime into the existing `Emberfall.Infrastructure` assembly, so this integration does not add another runtime assembly and cannot collide with the native `xlua.dll` filename.
