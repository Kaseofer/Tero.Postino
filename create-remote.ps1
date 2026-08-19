param(
	[Parameter(Mandatory=$true)]
	[string]$RepoName,
	[string]$Description = ""
)

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
	Write-Error "GitHub CLI 'gh' not found. Install and authenticate first."; exit 1
}

$owner = gh api user --jq .login
Write-Host "Creating repo $($owner)/$RepoName..."
gh repo create "$owner/$RepoName" --public --description "$Description" --confirm

git init
git add .
git commit -m "Initial scaffold"
git branch -M main
git remote add origin "git@github.com:$owner/$RepoName.git"
git push -u origin main
Write-Host "Remote repository created and initial commit pushed."
