# Jellyfin Audio Track Selector Plugin - Installation Guide

## Overview

This plugin automatically selects the optimal audio track for playback based on your client device capabilities. It prioritizes tracks that sound best AND can actually be decoded by your device.

**Key Features:**
- Intelligent audio track selection using weighted scoring (40% codec, 30% channels, 15% bitrate, 10% spatial, 5% language)
- Special handling for Apple TV/SwiftFin (excludes TrueHD, prefers DD+ Atmos)
- Compatible with ALL Jellyfin clients (server-side plugin)
- Works with Jellyfin 10.10.7

---

## Installation Methods

### Method 1: Install via Jellyfin Dashboard (Recommended)

This method requires hosting the plugin repository files on a web server.

#### Step 1: Host the Repository

You need to make `manifest.json` and the ZIP file accessible via HTTP/HTTPS. Choose one option:

**Option A: GitHub (Easiest)**

1. Create a new GitHub repository (e.g., `jellyfin-plugin-audioTrackselector`)

2. Upload these files to the repository:
   ```
   manifest.json
   jellyfin-plugin-audioTrackselector_1.0.0.0.zip
   ```

3. Create a GitHub Release:
   - Go to "Releases" → "Create a new release"
   - Tag version: `v1.0.0.0`
   - Upload `jellyfin-plugin-audioTrackselector_1.0.0.0.zip` as an attachment
   - Publish release

4. Update `manifest.json` `sourceUrl` to match your release URL:
   ```json
   "sourceUrl": "https://github.com/YOUR_USERNAME/YOUR_REPO/releases/download/v1.0.0.0/jellyfin-plugin-audioTrackselector_1.0.0.0.zip"
   ```

5. Commit and push the updated `manifest.json`

6. Get the raw manifest URL:
   ```
   https://raw.githubusercontent.com/YOUR_USERNAME/YOUR_REPO/main/manifest.json
   ```

**Option B: GitHub Pages**

1. Create a GitHub repository with GitHub Pages enabled

2. Upload files to the `docs/` folder or root (depending on your Pages settings)

3. Your manifest URL will be:
   ```
   https://YOUR_USERNAME.github.io/YOUR_REPO/manifest.json
   ```

**Option C: Your Own Web Server**

1. Upload both files to your web server

2. Ensure they're accessible via HTTP/HTTPS

3. Update the `sourceUrl` in `manifest.json` to point to your ZIP file location

4. Your manifest URL will be:
   ```
   https://yourdomain.com/path/to/manifest.json
   ```

#### Step 2: Add Repository to Jellyfin

1. Open Jellyfin Dashboard
2. Go to **Plugins** → **Repositories**
3. Click the **+** button
4. Fill in:
   - **Repository Name**: `Audio Track Selector`
   - **Repository URL**: `<your manifest.json URL from Step 1>`
5. Click **Save**

#### Step 3: Install the Plugin

1. Go to **Plugins** → **Catalog**
2. Find "Audio Track Selector" in the list
3. Click **Install**
4. Restart Jellyfin when prompted

---

### Method 2: Manual Installation (Advanced)

If you have SSH/terminal access to your Jellyfin server:

#### Step 1: Create Plugin Directory

```bash
# Create the plugin directory
sudo mkdir -p "/usr/local/@APP_CONFIG/Jellyfin/data/plugins/Audio Track Selector_1.0.0.0"
```

#### Step 2: Copy Plugin Files

```bash
# Extract and copy the DLL from the ZIP
unzip jellyfin-plugin-audioTrackselector_1.0.0.0.zip -d /tmp/plugin
sudo cp /tmp/plugin/Jellyfin.Plugin.AudioTrackSelector.dll "/usr/local/@APP_CONFIG/Jellyfin/data/plugins/Audio Track Selector_1.0.0.0/"
```

#### Step 3: Set Correct Permissions

Find the user/group that Jellyfin runs as (usually `jellyfin:jellyfin` or the user you're running Jellyfin as):

```bash
# Check what user owns other plugin directories
ls -la /usr/local/@APP_CONFIG/Jellyfin/data/plugins/

# Set ownership (replace USER:GROUP with actual values)
sudo chown -R USER:GROUP "/usr/local/@APP_CONFIG/Jellyfin/data/plugins/Audio Track Selector_1.0.0.0"

# Set proper permissions
sudo chmod 755 "/usr/local/@APP_CONFIG/Jellyfin/data/plugins/Audio Track Selector_1.0.0.0"
sudo chmod 644 "/usr/local/@APP_CONFIG/Jellyfin/data/plugins/Audio Track Selector_1.0.0.0/Jellyfin.Plugin.AudioTrackSelector.dll"
```

**IMPORTANT**: Jellyfin needs **write access** to the plugin directory to create `meta.json`. Make sure the directory is writable by the Jellyfin user.

#### Step 4: Restart Jellyfin

```bash
# Restart Jellyfin (command varies by installation method)
sudo systemctl restart jellyfin
# OR
sudo service jellyfin restart
# OR check your Jellyfin documentation
```

#### Step 5: Verify Installation

1. Check Jellyfin logs for plugin loading:
   ```bash
   tail -f /usr/local/@APP_CONFIG/Jellyfin/log/log_*.log | grep "AudioTrackSelector"
   ```

2. You should see:
   ```
   Loaded assembly "Jellyfin.Plugin.AudioTrackSelector, Version=1.0.0.0"
   Loaded plugin: "Audio Track Selector" "1.0.0.0"
   ```

3. Check Dashboard → Plugins to verify it appears in the list

---

## Troubleshooting

### Plugin Not Appearing in Dashboard After Manual Install

**Symptom**: Plugin directory exists, but plugin doesn't show up

**Solutions**:

1. **Check permissions on the entire directory**:
   ```bash
   ls -la /usr/local/@APP_CONFIG/Jellyfin/data/plugins/
   ```
   The "Audio Track Selector_1.0.0.0" directory should have the same owner/permissions as other plugins.

2. **Ensure Jellyfin can write to the directory**:
   ```bash
   sudo chmod 775 "/usr/local/@APP_CONFIG/Jellyfin/data/plugins/Audio Track Selector_1.0.0.0"
   ```

3. **Check Jellyfin logs** for errors:
   ```bash
   grep -i "AudioTrackSelector\|Audio Track Selector" /usr/local/@APP_CONFIG/Jellyfin/log/log_*.log
   ```

4. **Create meta.json manually** (if Jellyfin can't write it):
   ```bash
   sudo nano "/usr/local/@APP_CONFIG/Jellyfin/data/plugins/Audio Track Selector_1.0.0.0/meta.json"
   ```

   Add this content:
   ```json
   {
     "Name": "Audio Track Selector",
     "Guid": "a7f3d8e1-9c2b-4a5d-8f1e-3b4c5d6e7f8a",
     "Version": "1.0.0.0",
     "Status": "Active"
   }
   ```

   Then set permissions:
   ```bash
   sudo chown USER:GROUP "/usr/local/@APP_CONFIG/Jellyfin/data/plugins/Audio Track Selector_1.0.0.0/meta.json"
   sudo chmod 644 "/usr/local/@APP_CONFIG/Jellyfin/data/plugins/Audio Track Selector_1.0.0.0/meta.json"
   ```

### Permission Denied Errors

**Symptom**: `System.UnauthorizedAccessException: Access to the path '.../meta.json' is denied`

**Solution**: The Jellyfin user doesn't have write access to the plugin directory.

```bash
# Find Jellyfin's user
ps aux | grep jellyfin

# Set ownership recursively
sudo chown -R jellyfin:jellyfin "/usr/local/@APP_CONFIG/Jellyfin/data/plugins/Audio Track Selector_1.0.0.0"

# Make directory writable
sudo chmod 775 "/usr/local/@APP_CONFIG/Jellyfin/data/plugins/Audio Track Selector_1.0.0.0"
```

### Plugin Loads But Doesn't Select Audio Tracks

**Check**:

1. Plugin is enabled in Dashboard → Plugins
2. Jellyfin logs show the plugin services are registered:
   ```
   grep "AudioTrackSelectionService\|DeviceCapabilityMatcher" /path/to/jellyfin.log
   ```

3. Try playing a video with multiple audio tracks and check logs for selection activity

---

## How It Works

The plugin intercepts Jellyfin's media playback pipeline and:

1. **Analyzes available audio tracks** in your media file
2. **Queries client capabilities** from the device profile (what codecs/channels it supports)
3. **Filters incompatible tracks** (e.g., excludes TrueHD if client doesn't support it)
4. **Scores remaining tracks** using weighted algorithm:
   - Codec quality: 40% (lossless > high-quality lossy > standard)
   - Channel count: 30% (more channels = better, up to device max)
   - Bitrate: 15% (higher = better quality)
   - Spatial audio: 10% (Atmos/DTS:X bonus)
   - Language match: 5% (matches preferred language)
5. **Selects highest-scored track** for playback

### Example Selection

**Media file** with tracks:
- Track 1: TrueHD Atmos 7.1 (3.5 Mbps)
- Track 2: DD+ Atmos 5.1 (768 kbps)
- Track 3: AC3 5.1 (448 kbps)
- Track 4: AAC Stereo (192 kbps)

**Client**: SwiftFin on Apple TV (supports DD+, AC3, AAC; no TrueHD support; max 5.1 channels)

**Plugin selection**:
1. ❌ Track 1 filtered out (TrueHD not supported by SwiftFin)
2. ✅ Track 2 selected (DD+ Atmos: best quality + Atmos + compatible)
3. Track 3 scored lower (no Atmos, lower codec quality)
4. Track 4 scored lowest (stereo only)

**Result**: Plugin automatically selects Track 2 (DD+ Atmos 5.1) for best experience

---

## Configuration

Currently, the plugin uses sensible defaults:

- **Enabled**: Yes
- **Preferred Language**: English (eng)
- **False Positive Detection**: Enabled (logging only)

Future versions will include a configuration UI in the Dashboard.

---

## Support

- **Issues**: Check Jellyfin logs at `/usr/local/@APP_CONFIG/Jellyfin/log/`
- **Questions**: Create an issue in the GitHub repository
- **Logs**: Look for entries containing "AudioTrackSelector", "AudioTrackSelectionService", or "DeviceCapabilityMatcher"

---

## Files in This Package

- `manifest.json` - Jellyfin plugin repository manifest
- `jellyfin-plugin-audioTrackselector_1.0.0.0.zip` - Plugin DLL package
- `README.md` - This file

---

## Version History

### 1.0.0.0 (2026-02-13)
- Initial release
- Automatic audio track selection based on device profiles
- Weighted scoring algorithm
- Special handling for Apple TV/SwiftFin
- Compatible with Jellyfin 10.10.7
