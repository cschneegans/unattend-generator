using System.Collections.Immutable;

namespace Schneegans.Unattend;

public record class SystemSettings(
  bool EnableLongPaths,
  bool EnableRemoteDesktop,
  bool HardenSystemDriveAcl,
  bool AllowPowerShellScripts,
  bool DisableLastAccess,
  bool PreventAutomaticReboot,
  bool DisableDefender,
  bool DisableSac,
  bool DisableUac,
  bool DisableSmartScreen,
  bool DisableSystemRestore,
  bool DisableFastStartup,
  bool TurnOffSystemSounds,
  bool DisableAppSuggestions,
  bool PreventDeviceEncryption,
  bool DisableWindowsUpdate,
  bool DisablePointerPrecision,
  bool DeleteWindowsOld,
  bool DisableBingResults,
  bool UseConfigurationSet,
  bool HidePowerShellWindows,
  bool KeepSensitiveFiles,
  bool UseNarrator,
  bool DisableCoreIsolation,
  CompactOsModes CompactOsMode
);

public record class VisualSettings(
  bool ClassicContextMenu,
  bool LeftTaskbar,
  bool HideTaskViewButton,
  bool ShowFileExtensions,
  bool ShowAllTrayIcons,
  HideModes HideFiles,
  bool HideEdgeFre,
  bool DisableEdgeStartupBoost,
  bool MakeEdgeUninstallable,
  bool LaunchToThisPC,
  bool ShowEndTask,
  bool DisableWidgets,
  TaskbarSearchMode TaskbarSearch,
  IStartPinsSettings StartPinsSettings,
  IStartTilesSettings StartTilesSettings,
  ITaskbarIcons TaskbarIcons,
  IEffects Effects,
  IDesktopIconSettings DesktopIcons,
  IStartFolderSettings StartFolderSettings
);

public record class VirtualMachineSettings(
  bool VBoxGuestAdditions,
  bool VMwareTools,
  bool VirtIoGuestTools,
  bool ParallelsTools
);

public record class WingetSettings(
  ImmutableList<string> Packages
);

public record class Win32Settings(
  IWallpaperSettings WallpaperSettings,
  ILockScreenSettings LockScreenSettings,
  IColorSettings ColorSettings,
  ILockKeySettings LockKeySettings,
  IStickyKeysSettings StickyKeysSettings
);
