# Releasing hookjump for Windows

GitHub Actions builds the files from a tag, with the same tool a build on any
machine uses (`pack.cs`), and they are attached to a release by hand. The
release is made from the tag only once the files exist, so it never goes live
without them.

1. The commit to release is on `main`, and CI is green on it.
2. Tag it and push the tag (a plain tag, like the others):

   ```sh
   git tag windows-v0.1.0 <commit>
   git push origin windows-v0.1.0
   ```

   The "Windows release" workflow starts: it restores the locked packages,
   runs the tests, packs, and attests the zip, the exe and the dll.
3. When it has finished, download its artifact, named after the tag: it holds
   `hookjump-windows-0.1.0-x64.zip` and `SHA256SUMS.txt`. GitHub keeps
   artifacts for a limited time, so attach them soon.
4. Check them. Optional, but this is what lets an unsigned exe be trusted:
   - Build the same tag here, from a clean checkout of it, in `windows/`:
     `dotnet run release/pack.cs -- 0.1.0`, then compare
     `artifacts/SHA256SUMS.txt` with the downloaded one. The same commit
     gives the same bytes.
   - `gh attestation verify hookjump-windows-0.1.0-x64.zip --repo turhanGoksu/hookjump`
     confirms that this repository's workflow built the zip.
5. On GitHub, Releases › Draft a new release: choose the existing tag
   `windows-v0.1.0` (a tag typed in new there would point at the branch's
   latest commit, which may not be the one built), attach both files, and
   write the notes: what is new, what was not tried, and the SHA-256 of the
   zip. Keep it a draft until it reads right, then publish.
