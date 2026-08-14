# Shtraus Space

Unity project started with Unity 6000.3.22f1.

## Open the project

1. Install/open Unity Hub and sign in with a Unity account.
2. Add this folder as an existing project:
   `/home/che/my_script_project/Shtraus_Space`
3. Select Unity 6000.3.22f1 when prompted.

The repository intentionally excludes Unity-generated folders and the local Editor installation. See `.gitignore` for details.

## Git commands in this environment

The host supplied a read-only `.git` mount, so this checkout keeps its writable Git metadata in `.git-data/.git`. Use these equivalent commands from the project folder:

```bash
git --git-dir=.git-data/.git --work-tree=. status
git --git-dir=.git-data/.git --work-tree=. add .
git --git-dir=.git-data/.git --work-tree=. commit -m "Describe the change"
git --git-dir=.git-data/.git --work-tree=. push
```

The GitHub repository is private and available at `https://github.com/ddq771/Shtraus_Space`.

## Local Editor installed for this machine

The project-local tools folder contains Unity Hub 3.19.5 and Unity Editor 6000.3.22f1. These are local machine tooling and are not tracked by Git.
