# JellyFusion 🎬

**All-in-one Jellyfin enhancement plugin** — combines Editor's Choice, JellyTag, Studios, Themes and Notifications into a single unified plugin.

![JellyFusion Banner](assets/banner.png)

## ✨ Features

### 🎬 Editor's Choice Slider
- Netflix-style full-width banner on the home page
- Modes: **Favourites**, **Random**, **Collections**, **New releases**
- **Auto-trailer** via TMDB API — plays in the configured language (es-419/en/pt/fr) with subtitle fallback
- Filtering by community rating, critic rating, parental rating
- Hero display style, customizable height and transitions

### 🏷️ JellyTag Badges
- Server-side quality badges on **all Jellyfin clients** (no client config needed)
- **Resolution**: 4K, 1080p, 720p, SD
- **HDR**: Dolby Vision, HDR10+, HDR10, HLG
- **Codec**: HEVC, AV1, VP9, H.264
- **Audio**: Atmos, TrueHD, DTS:X, DTS-HD MA, 7.1, 5.1, Stereo
- **🌎 LAT/SUB** — Special Latin Spanish handling:
  - Detects `es-419` audio → shows 🇲🇽/🇪🇸 flag + **LAT** text
  - Shows production country flag alongside LAT
  - Collapses 3+ subtitle languages to a single **SUB** badge
- **🆕 NUEVO** — badge for recently added content (configurable days)
- **👶 KID** — badge for child-friendly content (by parental rating or tag)
- Custom badge images (SVG/PNG/JPEG) and text overrides
- Image cache with configurable duration

### 🎭 Studios Section
- Clickable studio logos displayed below the slider
- Clicking navigates to that studio's content in your library
- Add any studio with a custom logo (image/GIF URL)
- Configurable card size, border radius, hover effects

### 🎨 Theme Switcher
- **6 built-in themes**: Netflix, Prime Video, Disney+, Apple TV+, Crunchyroll, Paramount+
- Customizable primary color, background color, and banner font per theme

### 🔔 Notifications
- Notify on new content added
- Separate notification for new kid-friendly content
- **Discord** webhook support
- **Telegram** bot support

### 🌍 Multi-language UI
The plugin interface is fully translated into:
- 🌎 **Español Latino** (es)
- 🇬🇧 **English** (en)
- 🇧🇷 **Português** (pt)
- 🇫🇷 **Français** (fr)

---

## 📦 Installation

### Option 1 — Plugin repository (recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**
2. Add the following URL:
   ```
   https://raw.githubusercontent.com/YOUR_GITHUB_USERNAME/jellyfusion/main/manifest.json
   ```
3. Go to **Dashboard → Plugins → Catalog**
4. Find **JellyFusion** and install it
5. Restart Jellyfin

### Option 2 — Manual

1. Download the latest `.zip` from [Releases](https://github.com/YOUR_GITHUB_USERNAME/jellyfusion/releases)
2. Extract the `.dll` file into your Jellyfin plugins folder:
   - Linux: `/var/lib/jellyfin/plugins/JellyFusion/`
   - Windows: `%APPDATA%\Jellyfin\plugins\JellyFusion\`
   - Docker: `/config/plugins/JellyFusion/`
3. Restart Jellyfin

### Script injection setup

For the Editor's Choice slider to appear, you need one of:

**A) Automatic injection** (requires write permission on `jellyfin-web/index.html`)
- Enable **"Client script injection"** in plugin settings → Editor's Choice → Technical settings

**B) FileTransformation plugin** (recommended for Docker)
- Install the [FileTransformation plugin](https://www.iamparadox.dev/jellyfin/plugins/manifest.json) first
- Enable **"Use FileTransformation plugin"** in settings

**C) Manual injection**
- Open `jellyfin-web/index.html` and add before `</body>`:
  ```html
  <script plugin="JellyFusion" defer="defer" src="/jellyfusion/script"></script>
  ```

---

## ⚙️ Configuration

Go to **Dashboard → Plugins → JellyFusion** to access the full configuration UI.

### TMDB API Key (for trailers)

1. Create a free account at [themoviedb.org](https://www.themoviedb.org)
2. Go to Settings → API → Request an API Key
3. Paste the key in **Editor's Choice → Trailer → TMDB API Key**

---

## 🏗️ Building from source

Requirements: .NET 8 SDK

```bash
git clone https://github.com/YOUR_GITHUB_USERNAME/jellyfusion
cd jellyfusion
dotnet build src/JellyFusion/JellyFusion.csproj --configuration Release
```

---

## 📝 Changelog

### v1.0.0
- Initial release
- Editor's Choice slider with TMDB auto-trailers
- JellyTag badges: LAT/SUB/NUEVO/KID
- Studios section
- 6 theme presets
- Discord & Telegram notifications
- Multi-language UI (es/en/pt/fr)

---

## 🙏 Credits

- [Editor's Choice](https://github.com/lachlandcp/jellyfin-editors-choice-plugin) by lachlandcp — original slider implementation
- [JellyTag](https://github.com/Atilil/jellyfin-plugins) by Atilil — original badge system
- Built with ❤️ using Claude by Anthropic

---

## 📄 License

MIT License — see [LICENSE](LICENSE) file.
