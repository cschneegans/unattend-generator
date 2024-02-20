using System;

namespace Schneegans.Unattend;

class LockoutModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    CommandAppender appender = new(Document, NamespaceManager, CommandConfig.Specialize);

    if (Configuration.LockoutSettings is DefaultLockoutSettings)
    {
      return;
    }
    else if (Configuration.LockoutSettings is DisableLockoutSettings)
    {
      appender.Command(@"net.exe accounts /lockoutthreshold:0");
    }
    else if (Configuration.LockoutSettings is CustomLockoutSettings settings)
    {
      appender.Command($@"net.exe accounts /lockoutthreshold:{settings.LockoutThreshold} /lockoutduration:{settings.LockoutDuration} /lockoutwindow:{settings.LockoutWindow}");
    }
    else
    {
      throw new NotSupportedException();
    }
  }
}
