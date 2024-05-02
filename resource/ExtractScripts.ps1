param(
    [xml] $Document
);

$scriptsDir = 'C:\Windows\Setup\Scripts\';
foreach( $file in $Document.unattend.Extensions.File ) {
    $path = [System.Environment]::ExpandEnvironmentVariables(
        $file.GetAttribute( 'path' )
    );
    if( $path.StartsWith( $scriptsDir ) ) {
        mkdir -Path $scriptsDir -ErrorAction 'SilentlyContinue';
    }
    $encoding = switch( [System.IO.Path]::GetExtension( $path ) ) {
        { $_ -in '.ps1', '.xml' } { [System.Text.Encoding]::UTF8; }
        { $_ -in '.reg', '.vbs', '.js' } { [System.Text.UnicodeEncoding]::new( $false, $true ); }
        default { [System.Text.Encoding]::Default; }
    };
    [System.IO.File]::WriteAllBytes( $path, ( $encoding.GetPreamble() + $encoding.GetBytes( $file.InnerText.Trim() ) ) );
}