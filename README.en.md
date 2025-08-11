# LineageOS ROM Downloader

A command-line tool for downloading LineageOS ROMs and other build artifacts for your device.

This tool automates the process of fetching the latest builds from the official LineageOS download portal, verifying file integrity, and managing old builds.

## Features

- **Automatic Updates**: Fetches the latest build information directly from the LineageOS API.
- **Parallel Downloads**: Speeds up the download process by using multiple threads.
- **File Integrity Check**: Verifies the SHA-256 hash of every downloaded file to ensure it's not corrupted.
- **Automatic Cleanup**: Keeps your download directory tidy by automatically deleting old builds.
- **Flexible File Selection**: Allows you to specify which types of files to download (e.g., `rom`, `recovery`, `boot`).
- **Research Mode**: Lets you check all available file types for your device before downloading.

## Usage

### Basic Download

To download the latest ROM and recovery image for your device:

```bash
LineageOS_ROM_Downloader -d <device_codename> -p <download_path> -img rom -img recovery
```

### Command-Line Options

| Option     | Description                                                                                                 |
| :--------- | :---------------------------------------------------------------------------------------------------------- |
| `-d`       | **(Required)** The codename of your device (e.g., `renoir`, `pioneer`).                                       |
| `-p`       | **(Required)** The root path where files will be downloaded.                                                 |
| `-img`     | (Optional) The type of file to download. Can be specified multiple times. Common types are `rom`, `recovery`, `boot`. Use `--research` to find all available types. |
| `-mt`      | (Optional) The maximum number of parallel threads for downloading. Defaults to 1. Use with caution.         |
| `-k`       | (Optional) The number of recent builds to keep during cleanup. Defaults to 1.                               |
| `--research` | A special mode to list all available file types for a specific device without downloading anything.       |

### Examples

**Download the ROM and recovery for `renoir` to `./lineage_builds`:**
```bash
LineageOS_ROM_Downloader -d renoir -p ./lineage_builds -img rom -img recovery
```

**Download the ROM using 4 parallel threads and keep the last 3 builds:**
```bash
LineageOS_ROM_Downloader -d renoir -p ./lineage_builds -img rom -mt 4 -k 3
```

**Find out what file types are available for `pioneer`:**
```bash
LineageOS_ROM_Downloader --research -d pioneer
```

## Downloads

Instead of building it yourself, you can also download this tool directly from the [Releases](releases) page on GitHub.

The releases page provides self-contained executables for Windows, Linux, and macOS. These will work even without the .NET runtime installed. Please download the appropriate file for your operating system.

## How to Build

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Build Command

1.  Clone this repository.
2.  Open a terminal in the `src/LineageOS_ROM_Downloader` directory.
3.  Run the following command:

    ```bash
    dotnet publish -c Release
    ```

4.  The self-contained executable will be created in the `src/publish/` directory, organized by OS and architecture (e.g., `src/publish/net9.0/win-x64/`). The program can be run from anywhere without needing the .NET runtime installed.
