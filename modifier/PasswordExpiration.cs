using System;

namespace Schneegans.Unattend;

public interface IPasswordExpirationSettings;

public class DefaultPasswordExpirationSettings : IPasswordExpirationSettings
{
  public const int MaxAge = 42;
}

public class UnlimitedPasswordExpirationSettings : IPasswordExpirationSettings;

public class CustomPasswordExpirationSettings(int? maxAge) : IPasswordExpirationSettings
{
  public int MaxAge => Validation.InRange(maxAge, min: 1, max: 999);
}

class PasswordExpirationModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    CommandAppender appender = GetAppender(CommandConfig.Specialize);

    switch (Configuration.PasswordExpirationSettings)
    {
      case DefaultPasswordExpirationSettings:
        break;

      case UnlimitedPasswordExpirationSettings:
        appender.Append(
          CommandBuilder.Raw("net.exe accounts /maxpwage:UNLIMITED")
        );
        break;

      case CustomPasswordExpirationSettings settings:
        appender.Append(
          CommandBuilder.Raw($"net.exe accounts /maxpwage:{settings.MaxAge}")
        );
        break;

      default:
        throw new NotSupportedException();
    }
  }
}
