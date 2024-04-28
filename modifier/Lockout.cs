using System;

namespace Schneegans.Unattend;

public interface ILockoutSettings;

public class DefaultLockoutSettings : ILockoutSettings;

public class DisableLockoutSettings : ILockoutSettings;

public class CustomLockoutSettings : ILockoutSettings
{
  public CustomLockoutSettings(int? lockoutThreshold, int? lockoutDuration, int? lockoutWindow)
  {
    LockoutThreshold = Validation.InRange(lockoutThreshold, min: 0, max: 999); ;
    LockoutDuration = Validation.InRange(lockoutDuration, min: 1, max: 99_999); ;
    LockoutWindow = Validation.InRange(lockoutWindow, min: 1, max: 99_999); ;

    if (LockoutWindow > LockoutDuration)
    {
      throw new ConfigurationException($"Value of '{nameof(LockoutWindow)}' ({LockoutWindow}) must be less or equal to value of '{nameof(LockoutDuration)}' ({LockoutDuration}).");
    }
  }

  public int LockoutThreshold { get; }

  public int LockoutDuration { get; }

  public int LockoutWindow { get; }
}

class LockoutModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    CommandAppender appender = GetAppender(CommandConfig.Specialize);

    switch (Configuration.LockoutSettings)
    {
      case DefaultLockoutSettings:
        return;
      case DisableLockoutSettings:
        appender.Append(
          CommandBuilder.Raw("net.exe accounts /lockoutthreshold:0")
        );
        break;
      case CustomLockoutSettings settings:
        appender.Append(
          CommandBuilder.Raw($"net.exe accounts /lockoutthreshold:{settings.LockoutThreshold} /lockoutduration:{settings.LockoutDuration} /lockoutwindow:{settings.LockoutWindow}")
        );
        break;
      default:
        throw new NotSupportedException();
    }
  }
}
