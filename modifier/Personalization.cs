﻿using System.Drawing;
using System.IO;

namespace Schneegans.Unattend;

public interface IWallpaperSettings;

public class DefaultWallpaperSettings : IWallpaperSettings;

public record class SolidWallpaperSettings(
  Color Color
) : IWallpaperSettings;

public interface IColorSettings;

public class DefaultColorSettings : IColorSettings;

public enum ColorTheme
{
  Dark = 0,
  Light = 1
}

public record class CustomColorSettings(
  ColorTheme SystemTheme,
  ColorTheme AppsTheme,
  bool EnableTransparency,
  bool AccentColorOnStart,
  bool AccentColorOnBorders
) : IColorSettings;

class PersonalizationModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    CommandAppender appender = GetAppender(CommandConfig.Specialize);

    {
      if (Configuration.ColorSettings is CustomColorSettings settings)
      {
        string ps1File = @"C:\Windows\Setup\Scripts\SetColorTheme.ps1";
        string script = Util.StringFromResource("SetColorTheme.ps1");
        StringWriter writer = new();
        writer.WriteLine($"""
          $lightThemeSystem = {settings.SystemTheme:D};
          $lightThemeApps = {settings.AppsTheme:D};
          $accentColorOnStart = {(settings.AccentColorOnStart ? 1 : 0)};
          $enableTransparency = {(settings.EnableTransparency ? 1 : 0)};
          """);
        writer.WriteLine(script);
        AddTextFile(writer.ToString(), ps1File);
        appender.Append(
          CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
          {
            return [
              CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\SOFTWARE\Microsoft\Windows\DWM"" /v ColorPrevalence /t REG_DWORD /d {(settings.AccentColorOnBorders ? 1 : 0)} /f"),
              CommandBuilder.UserRunOnceCommand(rootKey, subKey, "SetColorTheme", CommandBuilder.InvokePowerShellScript(ps1File)),
            ];
          })
        );
      }
    }
    {
      if (Configuration.WallpaperSettings is SolidWallpaperSettings settings)
      {
        string ps1File = @"C:\Windows\Setup\Scripts\SetWallpaper.ps1";
        string script = Util.StringFromResource("SetWallpaper.ps1");
        StringWriter writer = new();
        writer.WriteLine($"$htmlColor = '{ColorTranslator.ToHtml(settings.Color)}';");
        writer.WriteLine(script);
        AddTextFile(writer.ToString(), ps1File);
        appender.Append(
          CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
          {
            return [
              CommandBuilder.UserRunOnceCommand(rootKey, subKey, "SetWallpaper", CommandBuilder.InvokePowerShellScript(ps1File)),
            ];
          })
        );
      }
    }
  }
}
