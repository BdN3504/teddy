You are helping the user update the current release.

Follow these steps:

1. Get the latest git tag using `git tag --sort=-v:refname | head -1`
2. Extract the version number from the tag (e.g., "v1.7.0" -> "1.7.0")
3. Delete the tag locally and remotely:
   - `git tag -d <tag>`
   - `git push origin :refs/tags/<tag>`
4. Ask the user what changes they want to make to the release notes for this version
5. Update the corresponding CHANGELOG file (CHANGELOG-v{version}.md) with the user's requested changes
6. Stage all current changes including the changelog: `git add -A`
7. Commit with message: "Update release notes for <tag>"
8. Push the changes: `git push origin master`
9. Recreate the tag with the same name: `git tag -a <tag> -m "Release <tag>"`
10. Push the tag to trigger the release pipeline: `git push origin <tag>`

After completing all steps, confirm to the user that the release has been updated and the pipeline should be triggered.