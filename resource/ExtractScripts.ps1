mkdir -Path "C:\Windows\Setup\Scripts" -ErrorAction 'SilentlyContinue';
$doc = [xml]::new(); $doc.Load( "C:\Windows\Panther\unattend.xml" );
foreach( $file in $doc.unattend.Extensions.File ) {
    $path = $file.GetAttribute( 'path' );
    $encoding = switch( [System.IO.Path]::GetExtension( $path ) ) {
        '.ps1' { [System.Text.Encoding]::UTF8; }
        '.cmd' { [System.Text.Encoding]::Default; }
        { $_ -in '.reg', '.vbs', '.js' } { [System.Text.UnicodeEncoding]::new( $false, $true ); }
    };
    [System.IO.File]::WriteAllBytes( $path, ( $encoding.GetPreamble() + $encoding.GetBytes( $file.InnerText ) ) );
}