# Contributing

## Line endings

This repository uses LF line endings for text files on every platform. Windows
command scripts (`.bat` and `.cmd`) are the only CRLF exceptions. The policy is
enforced by `.gitattributes`, while `.editorconfig` configures compatible
editors and IDEs.

Use repository-controlled line endings instead of Git's platform-dependent
conversion:

```sh
git config --global core.autocrlf false
git config --global core.safecrlf true
```

After changing line-ending rules, normalize the index once and review the
result before committing:

```sh
git add --renormalize .
git diff --cached --check
git diff --cached
```

The `External/bHapticsOSC` submodule is a separate repository with its own
`.gitattributes` and `.editorconfig`. Apply normalization and commit inside the
submodule before updating its commit pointer in this repository.

Do not commit generated Unity directories, build output, executables, DLLs,
debug symbols, or exported Unity packages. The existing ignore rules and CI
artifact audit cover these files.
