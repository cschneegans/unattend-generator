using System;
using System.Collections.Immutable;
using System.Linq;
using System.Xml;

namespace Schneegans.Unattend;

public interface IAccountSettings;

public class InteractiveAccountSettings : IAccountSettings;

public class UnattendedAccountSettings : IAccountSettings
{
  public UnattendedAccountSettings(
    ImmutableList<Account> accounts,
    IAutoLogonSettings autoLogonSettings
  )
  {
    Accounts = accounts;
    AutoLogonSettings = autoLogonSettings;

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
  public string Password => Validation.StringNotEmpty(password);
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
    {
      var passwordElem = NewElement("Password", container);
      NewSimpleElement("Value", passwordElem, password);
      NewSimpleElement("PlainText", passwordElem, "true");
    }
    {
      CommandAppender appender = new(Document, NamespaceManager, CommandConfig.Oobe);
      appender.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"" /v AutoLogonCount /t REG_DWORD /d 0 /f");
    }
  }

  private void AddUserAccounts(XmlElement container, UnattendedAccountSettings settings)
  {
    if (settings.AutoLogonSettings is BuiltinAutoLogonSettings bals)
    {
      XmlElement adminPassword = NewElement("AdministratorPassword", container);
      NewSimpleElement("Value", adminPassword, bals.Password);
      NewSimpleElement("PlainText", adminPassword, "true");
    }
    {
      XmlElement localAccounts = NewElement("LocalAccounts", container);
      foreach (Account account in settings.Accounts)
      {
        XmlElement localAccount = NewElement("LocalAccount", localAccounts);
        localAccount.SetAttribute("action", NamespaceManager.LookupNamespace("wcm"), "add");
        NewSimpleElement("Name", localAccount, account.Name);
        NewSimpleElement("Group", localAccount, account.Group);
        {
          XmlElement password = NewElement("Password", localAccount);
          NewSimpleElement("Value", password, account.Password);
          NewSimpleElement("PlainText", password, "true");
        }
      }
    }
  }
}
