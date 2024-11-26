param(
    [xml] $Document
);

foreach( $file in $Document.unattend.Extensions.File ) {
    $path = [System.Environment]::ExpandEnvironmentVariables(
        $file.GetAttribute( 'path' )
    );
    mkdir -Path( $path | Split-Path -Parent ) -ErrorAction 'SilentlyContinue';
    $content = $file.InnerText.Trim();
    if( $file.GetAttribute( 'transformation' ) -ieq 'Base64' ) {
        [System.IO.File]::WriteAllBytes( $path, [System.Convert]::FromBase64String( $content ) );
    } else {
        $encoding = switch( [System.IO.Path]::GetExtension( $path ) ) {
            { $_ -in '.ps1', '.xml' } { [System.Text.Encoding]::UTF8; }
            { $_ -in '.reg', '.vbs', '.js' } { [System.Text.UnicodeEncoding]::new( $false, $true ); }
            default { [System.Text.Encoding]::Default; }
        };
        [System.IO.File]::WriteAllBytes( $path, ( $encoding.GetPreamble() + $encoding.GetBytes( $content ) ) );
    }
}