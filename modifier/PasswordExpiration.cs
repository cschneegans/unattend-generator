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
    switch (Configuration.PasswordExpirationSettings)
    {
      case DefaultPasswordExpirationSettings:
        break;

      case UnlimitedPasswordExpirationSettings:
        SpecializeScript.Append("""
          Add-Type -TypeDefinition '
          	using System.Runtime.InteropServices;

          	public static class PasswordExpirationSetter {
          		[StructLayout(LayoutKind.Sequential)]
          		private struct USER_MODALS_INFO_1002 {
          			public uint usrmod1002_max_passwd_age;
          		}

          		[DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
          		private static extern int NetUserModalsSet(
          			string servername,
          			int level,
          			ref USER_MODALS_INFO_1002 buf,
          			out uint parm_err
          		);

          		public static void SetUnlimited() {
          			USER_MODALS_INFO_1002 info = new USER_MODALS_INFO_1002();
          			info.usrmod1002_max_passwd_age = uint.MaxValue;
          			uint parmErr;
          			int result = NetUserModalsSet(null, 1002, ref info, out parmErr);
          			if (result != 0) {
          				throw new System.ComponentModel.Win32Exception(result);
          			}
          		}
          	}
          ';
          [PasswordExpirationSetter]::SetUnlimited();
          """);
        break;

      case CustomPasswordExpirationSettings settings:
        SpecializeScript.Append($"net.exe accounts /maxpwage:{settings.MaxAge};");
        break;

      default:
        throw new NotSupportedException();
    }
  }
}
