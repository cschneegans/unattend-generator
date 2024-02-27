using System;
using System.Collections.Generic;
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
      string guid = Guid.Parse("d26bff32-33a2-48a3-b037-10357ee48427").ToString("B");
      string templateFile = @"C:\Windows\schemas\CodeIntegrity\ExamplePolicies\DefaultWindows_Enforced.xml";
      string activeFolder = @"C:\Windows\System32\CodeIntegrity\CiPolicies\Active";

      IEnumerable<string> GetInnerScript()
      {
        yield return $"Set-StrictMode -Version 'Latest';";
        yield return $"$ErrorActionPreference = 'Stop';";
        yield return $"$guid= '{guid}';";
        yield return $@"$xml = ""{activeFolder}\${{guid}}.xml"";";
        yield return $@"$binary = ""{activeFolder}\${{guid}}.cip"";";
        yield return $"Copy-Item -LiteralPath '{templateFile}' -Destination $xml;";

        {
          string SetRuleOption(byte option, bool delete = false)
          {
            StringBuilder builder = new();
            builder.Append($"Set-RuleOption -FilePath $xml -Option {option}");
            if (delete)
            {
              builder.Append(" -Delete");
            }
            builder.Append(';');
            return builder.ToString();
          }

          yield return SetRuleOption(0);
          yield return SetRuleOption(6);
          yield return SetRuleOption(9);
          yield return SetRuleOption(16);
          yield return SetRuleOption(18);
          yield return SetRuleOption(5, delete: true);

          if (settings.ScriptMode == WdacScriptModes.Unrestricted)
          {
            yield return SetRuleOption(11);
          }

          switch (settings.AuditMode)
          {
            case WdacAuditModes.Auditing:
              yield return SetRuleOption(3);
              yield return SetRuleOption(10);
              break;
            case WdacAuditModes.AuditingOnBootFailure:
              yield return SetRuleOption(10);
              break;
            case WdacAuditModes.Enforcement:
              break;
          }
        }

        {
          string NewPathRule(string path, RuleType type)
          {
            StringBuilder builder = new();
            builder.Append($"\t\tNew-CIPolicyRule -FilePathRule '{path}'");
            if (type == RuleType.Deny)
            {
              builder.Append(" -Deny");
            }
            builder.Append(';');
            return builder.ToString();
          }

          yield return "Merge-CIPolicy -PolicyPaths $xml -OutputFilePath $xml -Rules $(";
          yield return "\t@(";
          yield return NewPathRule(@"C:\Windows\*", RuleType.Allow);
          yield return NewPathRule(@"C:\Program Files\*", RuleType.Allow);
          yield return NewPathRule(@"C:\Program Files (x86)\*", RuleType.Allow);

          {
            using StreamReader sr = new(
              stream: Util.LoadFromResource("known-writeable-folders.txt"),
              encoding: Encoding.UTF8
            );

            foreach (string line in Util.SplitLines(sr))
            {
              yield return NewPathRule(line, RuleType.Deny);
            }
          }

          yield return "\t) | ForEach-Object -Process { $_; }";
          yield return ");";
        }
        {
          yield return "$doc = [xml]::new();";
          yield return "$doc.Load( $xml );";
          yield return "$nsmgr = [System.Xml.XmlNamespaceManager]::new( $doc.NameTable );";
          yield return "$nsmgr.AddNamespace( 'pol', 'urn:schemas-microsoft-com:sipolicy' );";
          yield return "$doc.SelectSingleNode( '/pol:SiPolicy/pol:PolicyID', $nsmgr ).InnerText = $guid;";
          yield return "$doc.SelectSingleNode( '/pol:SiPolicy/pol:BasePolicyID', $nsmgr ).InnerText = $guid;";
          yield return "$node = $doc.SelectSingleNode( '//pol:SigningScenario[@Value=''12'']/pol:ProductSigners/pol:AllowedSigners', $nsmgr );";
          yield return "$node.ParentNode.RemoveChild( $node );";
          yield return "$doc.Save( $xml );";
        }
        yield return @"ConvertFrom-CIPolicy -XmlFilePath $xml -BinaryFilePath $binary;";
      }

      IEnumerable<string> GetOuterScript()
      {
        yield return "$( try {";
        foreach (var line in GetInnerScript())
        {
          yield return "\t" + line;
        }
        yield return @"} catch { $_ } ) *>&1 | Out-File -Append -FilePath ""$env:TEMP\wdac.log"";";
      }

      CommandAppender appender = new(Document, NamespaceManager, CommandConfig.Specialize);
      string ps1File = @"%TEMP%\wdac.ps1";
      appender.WriteToFile(ps1File, GetOuterScript());
      appender.InvokePowerShellScript(ps1File);
    }
    else
    {
      throw new NotSupportedException();
    }
  }
}
