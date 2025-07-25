# Release Process

This project uses GitHub Actions to automatically build and publish releases for multiple platforms.

## Supported Platforms

The automated build creates executables for:
- **Windows x64** - Most Windows PCs (Intel/AMD 64-bit)
- **Windows ARM64** - Windows on ARM devices (Surface Pro X, etc.)

## Creating a Release

1. **Push your changes** to the main branch
2. **Create a new release** on GitHub:
   - Go to your repository on GitHub
   - Click "Releases" → "Create a new release"
   - Create a new tag (e.g., `v1.0.0`)
   - Add a release title and description
   - Click "Publish release"

3. **Wait for the build** - GitHub Actions will automatically:
   - Build the application for all platforms
   - Create compressed archives
   - Attach them to your release

## Local Testing

To test builds locally before releasing:

```powershell
# Build for Windows x64 (default)
.\build-release.ps1

# Build for Windows ARM64
.\build-release.ps1 -Runtime win-arm64
```

## Distribution

Once the release is published, your testers can:
1. Go to the Releases page of your repository
2. Download the appropriate `.zip` file for their Windows system:
   - `RoWheelBridge-win-x64.zip` for most Windows PCs
   - `RoWheelBridge-win-arm64.zip` for Windows ARM devices
3. Extract and run the executable

## Notes

- All builds are self-contained (no .NET runtime required)
- Single-file executables for easy distribution
- Trimmed builds for smaller file sizes
- Requires Windows 10/11 or later
- Works with DirectInput steering wheels and ViGEm virtual controllers