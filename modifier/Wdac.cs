using System;
using System.IO;
using System.Text;

namespace Schneegans.Unattend;

public interface IWdacSettings;

public class SkipWdacSettings : IWdacSettings;

public enum WdacScriptModes
{
  Restricted, Unrestricted
}

public enum WdacAuditModes
{
  Auditing, AuditingOnBootFailure, Enforcement
}

public record class ConfigureWdacSettings(
  WdacAuditModes AuditMode,
  WdacScriptModes ScriptMode
) : IWdacSettings;

enum RuleType
{
  Allow, Deny
}

class WdacModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.WdacSettings is SkipWdacSettings)
    {
      return;
    }
    else if (Configuration.WdacSettings is ConfigureWdacSettings settings)
    {
      StringWriter sw = new();
      string guid = Guid.Parse("d26bff32-33a2-48a3-b037-10357ee48427").ToString("B");
      string templateFile = @"C:\Windows\schemas\CodeIntegrity\ExamplePolicies\DefaultWindows_Enforced.xml";
      string activeFolder = @"C:\Windows\System32\CodeIntegrity\CiPolicies\Active";

      void SetRuleOption(byte option, bool delete = false)
      {
        sw.WriteLine(@$"    Set-RuleOption -FilePath $xml -Option {option}{(delete ? " -Delete" : "")};");
      }

      void NewPathRule(string path, RuleType type)
      {
        sw.WriteLine($"        New-CIPolicyRule -FilePathRule '{path}'{(type == RuleType.Deny ? " -Deny" : "")};");
      }

      sw.WriteLine($$"""
          Set-StrictMode -Version 'Latest';
          $ErrorActionPreference = 'Stop';
          $(
            try {
              $guid = '{{guid}}';
              $xml = "{{activeFolder}}\${guid}.xml";
              $binary = "{{activeFolder}}\${guid}.cip";
              Copy-Item -LiteralPath '{{templateFile}}' -Destination $xml;
          """
      );

      SetRuleOption(0);
      SetRuleOption(6);
      SetRuleOption(9);
      SetRuleOption(16);
      SetRuleOption(18);
      SetRuleOption(5, delete: true);

      if (settings.ScriptMode == WdacScriptModes.Unrestricted)
      {
        SetRuleOption(11);
      }

      switch (settings.AuditMode)
      {
        case WdacAuditModes.Auditing:
          SetRuleOption(3);
          SetRuleOption(10);
          break;
        case WdacAuditModes.AuditingOnBootFailure:
          SetRuleOption(10);
          break;
        case WdacAuditModes.Enforcement:
          break;
      }

      sw.WriteLine("""
            Merge-CIPolicy -PolicyPaths $xml -OutputFilePath $xml -Rules $(
              @(
        """
      );

      NewPathRule(@"C:\Windows\*", RuleType.Allow);
      NewPathRule(@"C:\Program Files\*", RuleType.Allow);
      NewPathRule(@"C:\Program Files (x86)\*", RuleType.Allow);

      {
        using StreamReader sr = new(
          stream: Util.LoadFromResource("known-writeable-folders.txt"),
          encoding: Encoding.UTF8
        );

        foreach (string line in Util.SplitLines(sr))
        {
          NewPathRule(line, RuleType.Deny);
        }
      }

      sw.WriteLine("""
              ) | ForEach-Object -Process {
                $_;
              };
            );
            $doc = [xml]::new();
            $doc.Load( $xml );
            $nsmgr = [System.Xml.XmlNamespaceManager]::new( $doc.NameTable );
            $nsmgr.AddNamespace( 'pol', 'urn:schemas-microsoft-com:sipolicy' );
            $doc.SelectSingleNode( '/pol:SiPolicy/pol:PolicyID', $nsmgr ).InnerText = $guid;
            $doc.SelectSingleNode( '/pol:SiPolicy/pol:BasePolicyID', $nsmgr ).InnerText = $guid;
            $node = $doc.SelectSingleNode( '//pol:SigningScenario[@Value="12"]/pol:ProductSigners/pol:AllowedSigners', $nsmgr );
            $node.ParentNode.RemoveChild( $node );
            $doc.Save( $xml );
            ConvertFrom-CIPolicy -XmlFilePath $xml -BinaryFilePath $binary;
          } catch {
            $_;
          }
        ) *>&1 | Out-File -Append -FilePath "$env:TEMP\wdac.log";
        """);

      CommandAppender appender = GetAppender(CommandConfig.Specialize);
      string ps1File = @"%TEMP%\wdac.ps1";
      AddTextFile(sw.ToString(), ps1File);
      appender.Append(
        CommandBuilder.InvokePowerShellScript(ps1File)
      );
    }
    else
    {
      throw new NotSupportedException();
    }
  }
}
