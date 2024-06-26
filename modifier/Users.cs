using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Xml;

namespace Schneegans.Unattend;

public interface IAccountSettings;

public class InteractiveAccountSettings : IAccountSettings;

public class UnattendedAccountSettings : IAccountSettings
{
  public UnattendedAccountSettings(
    ImmutableList<Account> accounts,
    IAutoLogonSettings autoLogonSettings,
    bool obscurePasswords
  )
  {
    Accounts = accounts;
    AutoLogonSettings = autoLogonSettings;
    ObscurePasswords = obscurePasswords;

    CheckAdministratorAccount();
    CheckUniqueNames();
  }

  private void CheckUniqueNames()
  {
    var collisions = Accounts
      .GroupBy(keySelector: account => account.Name, comparer: StringComparer.OrdinalIgnoreCase)
      .Where(group => group.Count() > 1)
      .Select(group => $"'{group.Key}'");

    if (collisions.Any())
    {
      throw new ConfigurationException($"Account name(s) {string.Join(", ", collisions)} specified more than once.");
    }
  }

  public ImmutableList<Account> Accounts { get; }

  public IAutoLogonSettings AutoLogonSettings { get; }

  public bool ObscurePasswords { get; }

  private void CheckAdministratorAccount()
  {
    if (AutoLogonSettings is BuiltinAutoLogonSettings)
    {
      return;
    }
    foreach (var account in Accounts)
    {
      if (account.Group == Constants.AdministratorsGroup)
      {
        return;
      }
    }

    throw new ConfigurationException("Must have at least one administrator account.");
  }
}

public interface IAutoLogonSettings;

public class NoneAutoLogonSettings : IAutoLogonSettings;

public class BuiltinAutoLogonSettings(
  string password
) : IAutoLogonSettings
{
  public string Password => password;
}

public class OwnAutoLogonSettings : IAutoLogonSettings;

public class Account
{
  public Account(
    string name,
    string password,
    string group
  )
  {
    Name = name;
    Password = password;
    Group = group;
    ValidateUsername();
  }

  public string Name { get; }

  public string Password { get; }

  public string Group { get; }

  private void ValidateUsername()
  {
    void Throw()
    {
      throw new ConfigurationException($"Username '{Name}' is invalid.");
    }

    if (string.IsNullOrWhiteSpace(Name))
    {
      Throw();
    }

    if (Name != Name.Trim())
    {
      Throw();
    }

    if (Name.Length > 20)
    {
      Throw();
    }

    if (Name.IndexOfAny(['/', '\\', '[', ']', ':', ';', '|', '=', ',', '+', '*', '?', '<', '>']) > -1)
    {
      Throw();
    }

    {
      string[] existing = [
        "administrator",
        "guest",
        "defaultaccount",
        "system",
        "network service",
        "local service"
      ];

      if (existing.Contains(Name, StringComparer.OrdinalIgnoreCase))
      {
        Throw();
      }
    }
  }
}

class UsersModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    switch (Configuration.AccountSettings)
    {
      case UnattendedAccountSettings settings:
        CheckComputerNameCollision(settings);
        AddAutoLogon((XmlElement)Document.SelectSingleNodeOrThrow("//u:AutoLogon", NamespaceManager), settings);
        AddUserAccounts((XmlElement)Document.SelectSingleNodeOrThrow("//u:UserAccounts", NamespaceManager), settings);
        break;
      case InteractiveAccountSettings:
        Document.SelectSingleNodeOrThrow("//u:AutoLogon", NamespaceManager).RemoveSelf();
        Document.SelectSingleNodeOrThrow("//u:UserAccounts", NamespaceManager).RemoveSelf();
        break;
      default:
        throw new NotSupportedException();
    }
  }

  private void CheckComputerNameCollision(UnattendedAccountSettings settings)
  {
    if (Configuration.ComputerNameSettings is CustomComputerNameSettings computer)
    {
      foreach (var account in settings.Accounts)
      {
        if (string.Equals(account.Name, computer.ComputerName, StringComparison.OrdinalIgnoreCase))
        {
          throw new ConfigurationException($"Account name '{account.Name}' must not be the same as the computer name.");
        }
      }
    }
  }

  private void AddAutoLogon(XmlElement container, UnattendedAccountSettings settings)
  {
    if (settings.AutoLogonSettings is NoneAutoLogonSettings)
    {
      return;
    }

    (string, string) GetAutoLogonCredentials()
    {
      switch (settings.AutoLogonSettings)
      {
        case BuiltinAutoLogonSettings bals:
          return ("Administrator", bals.Password);
        case OwnAutoLogonSettings oals:
          Account first = settings.Accounts
            .Where(a => a.Group == Constants.AdministratorsGroup)
            .First();
          return (first.Name, first.Password);
        default:
          throw new NotSupportedException();
      }
    }

    (string username, string password) = GetAutoLogonCredentials();

    NewSimpleElement("Username", container, username);
    NewSimpleElement("Enabled", container, "true");
    NewSimpleElement("LogonCount", container, "1");
    NewPasswordElement(container, "Password", password, settings.ObscurePasswords);

    {
      CommandAppender appender = GetAppender(CommandConfig.Oobe);
      appender.Append(
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"" /v AutoLogonCount /t REG_DWORD /d 0 /f")
      );
    }
  }

  private void NewPasswordElement(XmlElement parent, string element, string password, bool obscurePasswords)
  {
    var elem = NewElement(element, parent);
    if (obscurePasswords)
    {
      password = Convert.ToBase64String(Encoding.Unicode.GetBytes(password + element));
    }
    NewSimpleElement("Value", elem, password);
    NewSimpleElement("PlainText", elem, obscurePasswords ? "false" : "true");
  }

  private void AddUserAccounts(XmlElement container, UnattendedAccountSettings settings)
  {
    if (settings.AutoLogonSettings is BuiltinAutoLogonSettings bals)
    {
      NewPasswordElement(container, "AdministratorPassword", bals.Password, settings.ObscurePasswords);
    }
    {
      XmlElement localAccounts = NewElement("LocalAccounts", container);
      foreach (Account account in settings.Accounts)
      {
        XmlElement localAccount = NewElement("LocalAccount", localAccounts);
        localAccount.SetAttribute("action", NamespaceManager.LookupNamespace("wcm"), "add");
        NewSimpleElement("Name", localAccount, account.Name);
        NewSimpleElement("Group", localAccount, account.Group);
        NewPasswordElement(localAccount, "Password", account.Password, settings.ObscurePasswords);
      }
    }
  }
}
