using System;
using System.Drawing;
using System.IO;

namespace Schneegans.Unattend;

public interface IWallpaperSettings;

public class DefaultWallpaperSettings : IWallpaperSettings;

public record class SolidWallpaperSettings(
  Color Color
) : IWallpaperSettings;

public record class ScriptWallpaperSettings(
  string Script
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
  bool AccentColorOnBorders,
  Color AccentColor
) : IColorSettings;

public interface ILockScreenSettings;

public class DefaultLockScreenSettings : ILockScreenSettings;

public record class ScriptLockScreenSettings(
  string Script
) : ILockScreenSettings;

class PersonalizationModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    {
      if (Configuration.ColorSettings is CustomColorSettings settings)
      {
        string ps1File = EmbedTextFileFromResource("SetColorTheme.ps1", before: writer =>
        {
          writer.WriteLine($"""
            $lightThemeSystem = {settings.SystemTheme:D};
            $lightThemeApps = {settings.AppsTheme:D};
            $accentColorOnStart = {(settings.AccentColorOnStart ? 1 : 0)};
            $enableTransparency = {(settings.EnableTransparency ? 1 : 0)};
            $htmlAccentColor = '{ColorTranslator.ToHtml(settings.AccentColor)}';
            """);
        });
        DefaultUserScript.Append(@$"reg.exe add ""HKU\DefaultUser\Software\Microsoft\Windows\DWM"" /v ColorPrevalence /t REG_DWORD /d {(settings.AccentColorOnBorders ? 1 : 0)} /f;");
        UserOnceScript.InvokeFile(ps1File);
        UserOnceScript.RestartExplorer();
      }
    }
    {
      void WriteWallpaperScript(Action<StringWriter> after)
      {
        string ps1File = EmbedTextFileFromResource("SetWallpaper.ps1", after: after);
        UserOnceScript.InvokeFile(ps1File);
      }

      switch (Configuration.WallpaperSettings)
      {
        case ScriptWallpaperSettings settings:
          string imageFile = @"C:\Windows\Setup\Scripts\Wallpaper";
          string getterFile = EmbedTextFile("GetWallpaper.ps1", settings.Script);
          SpecializeScript.Append($$"""
            try {
              $bytes = & '{{getterFile}}';
              [System.IO.File]::WriteAllBytes( '{{imageFile}}', $bytes );
            } catch {
              $_;
            }
            """);
          WriteWallpaperScript(writer =>
          {
            writer.WriteLine(@$"Set-WallpaperImage -LiteralPath '{imageFile}';");
          });
          break;

        case SolidWallpaperSettings settings:
          WriteWallpaperScript(writer =>
          {
            writer.WriteLine($"Set-WallpaperColor -HtmlColor '{ColorTranslator.ToHtml(settings.Color)}';");
          });
          break;
      }
    }
    {
      switch (Configuration.LockScreenSettings)
      {
        case ScriptLockScreenSettings settings:
          string imageFile = @"C:\Windows\Setup\Scripts\LockScreenImage";
          string getterFile = EmbedTextFile("GetLockScreenImage.ps1", settings.Script);
          SpecializeScript.Append($$"""
            try {
              $bytes = & '{{getterFile}}';
              [System.IO.File]::WriteAllBytes( '{{imageFile}}', $bytes );
              reg.exe add "HKLM\Software\Microsoft\Windows\CurrentVersion\PersonalizationCSP" /v LockScreenImagePath /t REG_SZ /d "{{imageFile}}" /f;
            } catch {
              $_;
            }
            """);
          break;
      }
    }
  }
}
