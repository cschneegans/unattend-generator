using System;
using System.Linq;
using System.Xml;

namespace Schneegans.Unattend;

class UsersModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.AccountSettings is UnattendedAccountSettings settings)
    {
      AddAutoLogon((XmlElement)Document.SelectSingleNodeOrThrow("//u:AutoLogon", NamespaceManager), settings);
      AddUserAccounts((XmlElement)Document.SelectSingleNodeOrThrow("//u:UserAccounts", NamespaceManager), settings);
    }
    else if (Configuration.AccountSettings is InteractiveAccountSettings)
    {
      Document.SelectSingleNodeOrThrow("//u:AutoLogon", NamespaceManager).RemoveSelf();
      Document.SelectSingleNodeOrThrow("//u:UserAccounts", NamespaceManager).RemoveSelf();
    }
    else
    {
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
      if (settings.AutoLogonSettings is BuiltinAutoLogonSettings bals)
      {
        return ("Administrator", bals.Password);
      }
      else if (settings.AutoLogonSettings is OwnAutoLogonSettings oals)
      {
        Account first = settings.Accounts.Where(a => a.Group == Constants.AdministratorsGroup).First();
        return (first.Name, first.Password);
      }
      else
      {
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
      foreach (Account account in settings.Accounts.Where(account => account.HasName))
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
