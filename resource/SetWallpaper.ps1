Add-Type -TypeDefinition '
	using System.Drawing;
	using System.Runtime.InteropServices;
	
	public static class WallpaperSetter {
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

		public static void SetDesktopImage(string file) {
			SystemParametersInfo(20, 0, file, 0);
		}
	}
' -ReferencedAssemblies 'System.Drawing';

function Set-WallpaperColor {
	param(
		[string]
		$HtmlColor
	);

	$color = [System.Drawing.ColorTranslator]::FromHtml( $HtmlColor );
	[WallpaperSetter]::SetDesktopBackground( $color );
	Set-ItemProperty -Path 'Registry::HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers' -Name 'BackgroundType' -Type 'DWord' -Value 1 -Force;
	Set-ItemProperty -Path 'Registry::HKCU\Control Panel\Desktop' -Name 'WallPaper' -Type 'String' -Value '' -Force;
	Set-ItemProperty -Path 'Registry::HKCU\Control Panel\Colors' -Name 'Background' -Type 'String' -Value "$($color.R) $($color.G) $($color.B)" -Force;
}

function Set-WallpaperImage {
	param(
		[string]
		$LiteralPath
	);

	[WallpaperSetter]::SetDesktopImage( $LiteralPath );
	Set-ItemProperty -Path 'Registry::HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers' -Name 'BackgroundType' -Type 'DWord' -Value 0 -Force;
	Set-ItemProperty -Path 'Registry::HKCU\Control Panel\Desktop' -Name 'WallPaper' -Type 'String' -Value $LiteralPath -Force;
}