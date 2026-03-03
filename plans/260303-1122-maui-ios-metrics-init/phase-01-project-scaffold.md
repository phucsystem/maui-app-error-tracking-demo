---
phase: 1
title: "Project Scaffold"
status: complete
effort: 1.5h
---

# Phase 1 — Project Scaffold

## Overview

Initialize a .NET 9 MAUI iOS-only project, install NuGet packages, configure `Info.plist`, and add a `GoogleService-Info.plist` placeholder.

## Requirements

- .NET 9 SDK installed (`dotnet --version` ≥ 9.0)
- Xcode 16+ installed (for iOS simulator/device build)
- Firebase project created with iOS app registered

---

## Step 1 — Create the MAUI Project

```bash
# From repo root
dotnet new maui -n MauiFirebaseMetrics --framework net9.0

# Rename folder to match repo name convention
mv MauiFirebaseMetrics src
```

**Slim the project to iOS only** — edit `src/MauiFirebaseMetrics.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0-ios</TargetFrameworks>
    <RootNamespace>MauiFirebaseMetrics</RootNamespace>
    <ApplicationId>com.medadvisor.webmetrics</ApplicationId>
    <ApplicationVersion>1</ApplicationVersion>
    <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
    <SupportedOSPlatformVersion>16.0</SupportedOSPlatformVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <!-- NuGet packages added in Step 2 -->
</Project>
```

Remove Android/Windows/MacCatalyst platform folders:
```bash
rm -rf src/Platforms/Android
rm -rf src/Platforms/MacCatalyst
rm -rf src/Platforms/Tizen
rm -rf src/Platforms/Windows
```

---

## Step 2 — Install NuGet Packages

Add to `src/MauiFirebaseMetrics.csproj`:

```xml
<ItemGroup>
  <!-- Firebase Crashlytics for .NET MAUI -->
  <PackageReference Include="Plugin.Firebase.Crashlytics" Version="3.0.2" />

  <!-- Required Firebase core transitive (Plugin.Firebase pulls these, explicit pin for clarity) -->
  <!-- Plugin.Firebase.Core is a transitive dep — no explicit ref needed -->
</ItemGroup>
```

Run restore:
```bash
cd src && dotnet restore
```

**Package rationale:**
- `Plugin.Firebase.Crashlytics` 3.x — TobiasBuchholz's actively maintained .NET MAUI port
- Wraps native Firebase iOS SDK via .NET binding
- Supports both iOS and Android; iOS-only project will only link iOS native libs

---

## Step 3 — GoogleService-Info.plist

1. In Firebase Console → Project Settings → iOS app → download `GoogleService-Info.plist`
2. Place at `src/Platforms/iOS/GoogleService-Info.plist`
3. Add to `.csproj` with `BundleResource` build action:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">
  <BundleResource Include="Platforms\iOS\GoogleService-Info.plist" />
</ItemGroup>
```

4. Add to `.gitignore` (contains API keys):
```
src/Platforms/iOS/GoogleService-Info.plist
```

**Placeholder for CI/CD** — create `src/Platforms/iOS/GoogleService-Info.plist.template`:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>API_KEY</key><string>REPLACE_ME</string>
  <key>BUNDLE_ID</key><string>com.medadvisor.webmetrics</string>
  <key>PROJECT_ID</key><string>REPLACE_ME</string>
  <key>GCM_SENDER_ID</key><string>REPLACE_ME</string>
  <key>GOOGLE_APP_ID</key><string>REPLACE_ME</string>
  <key>IS_CRASHLYTICS_ENABLED</key><true/>
</dict>
</plist>
```

---

## Step 4 — Info.plist Configuration

`src/Platforms/iOS/Info.plist` — minimum required keys:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>WebMetrics</string>
  <key>CFBundleIdentifier</key>
  <string>com.medadvisor.webmetrics</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>MinimumOSVersion</key>
  <string>16.0</string>
  <key>UIRequiredDeviceCapabilities</key>
  <array><string>arm64</string></array>

  <!-- Allow arbitrary loads for external WebView URLs (adjust per domain policy) -->
  <key>NSAppTransportSecurity</key>
  <dict>
    <key>NSAllowsArbitraryLoads</key>
    <true/>
  </dict>
</dict>
</plist>
```

---

## Step 5 — PrivacyInfo.xcprivacy

Required for App Store submissions (iOS 17+). Create `src/Platforms/iOS/PrivacyInfo.xcprivacy`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>NSPrivacyCollectedDataTypes</key>
  <array>
    <dict>
      <key>NSPrivacyCollectedDataType</key>
      <string>NSPrivacyCollectedDataTypeCrashData</string>
      <key>NSPrivacyCollectedDataTypeLinked</key>
      <false/>
      <key>NSPrivacyCollectedDataTypeTracking</key>
      <false/>
      <key>NSPrivacyCollectedDataTypePurposes</key>
      <array>
        <string>NSPrivacyCollectedDataTypePurposeAppFunctionality</string>
      </array>
    </dict>
  </array>
  <key>NSPrivacyAccessedAPITypes</key>
  <array/>
</dict>
</plist>
```

Add to `.csproj`:
```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">
  <BundleResource Include="Platforms\iOS\PrivacyInfo.xcprivacy" />
</ItemGroup>
```

---

## Step 6 — Folder Structure Setup

Create placeholder files (content filled in later phases):

```bash
mkdir -p src/Services
mkdir -p src/Pages
touch src/Services/CrashService.cs
touch src/Services/PerformanceService.cs
touch src/Services/DownloadService.cs
touch src/Pages/MainPage.xaml
touch src/Pages/MainPage.xaml.cs
```

---

## Success Criteria

- [ ] `dotnet build src -f net9.0-ios` compiles without errors
- [ ] `GoogleService-Info.plist.template` committed; actual plist gitignored
- [ ] `Info.plist` has correct bundle ID and iOS 16.0 minimum
- [ ] `PrivacyInfo.xcprivacy` present for App Store compliance
- [ ] All Android/Windows platform folders removed

## Risks

| Risk | Mitigation |
|------|-----------|
| `Plugin.Firebase.Crashlytics` version incompatibility with .NET 9 | Check NuGet page; use latest stable 3.x |
| Xcode 16 linker issues with Firebase native XCFramework | Ensure `Plugin.Firebase` uses Firebase iOS SDK 11.x (Xcode 16 compatible) |
| NSAppTransportSecurity rejection by App Store | Replace `NSAllowsArbitraryLoads` with domain-specific exceptions if URLs are known |
