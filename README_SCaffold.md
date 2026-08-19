# Scaffold usage

Run the provided PowerShell script to create the remote repository and push the scaffold initial content. After that add the repo as a submodule in your monorepo/apphost using:

```powershell
git submodule add git@github.com:YOUR_ORG/Tero.Postino.git services/postino
git commit -m "Add postino submodule"
```

Adjust CI/CD and secrets as needed.
