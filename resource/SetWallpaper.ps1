Add-Type -TypeDefinition '
	using System.Drawing;
	using System.Runtime.InteropServices;
	
	public static class ColorSetter {
		[DllImport("user32.dll")]
		private static extern bool SetSysColors(
			int cElements, 
			int[] lpaElements,
			int[] lpaRgbValues
		);

		[DllImport("user32.dll")]
		private static extern bool SystemParametersInfo(
			uint uiAction,
			uint uiParam,
			string pvParam,
			uint fWinIni
		);

		public static void SetDesktopBackground(Color color) {
			SystemParametersInfo(20, 0, "", 0);
			SetSysColors(1, new int[] { 1 }, new int[] { ColorTranslator.ToWin32(color) });
		}
	}
' -ReferencedAssemblies 'System.Drawing';

$color = [System.Drawing.ColorTranslator]::FromHtml( $htmlColor );
[ColorSetter]::SetDesktopBackground( $color );
Set-ItemProperty -Path 'Registry::HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers' -Name 'BackgroundType' -Type 'DWord' -Value 1 -Force;
Set-ItemProperty -Path 'Registry::HKCU\Control Panel\Desktop' -Name 'WallPaper' -Type 'String' -Value '' -Force;
Set-ItemProperty -Path 'Registry::HKCU\Control Panel\Colors' -Name 'Background' -Type 'String' -Value "$($color.R) $($color.G) $($color.B)" -Force;