mkdir -Path "C:\Windows\Setup\Scripts" -ErrorAction 'SilentlyContinue';
$doc = [xml]::new();
$doc.Load( "C:\Windows\Panther\unattend.xml" );
$ns = [System.Xml.XmlNamespaceManager]::new($doc.NameTable);
$ns.AddNamespace( 's', 'https://schneegans.de/windows/unattend-generator/' );
foreach( $file in $doc.SelectNodes( '//s:File[@path]', $ns ) ) {
    $path = $file.GetAttribute( 'path' );
    $encoding = switch( [System.IO.Path]::GetExtension( $path ) ) {
        '.ps1' { [System.Text.Encoding]::UTF8; }
        '.cmd' { [System.Text.Encoding]::Default; }
        { $_ -in '.reg', '.vbs', '.js' } { [System.Text.UnicodeEncoding]::new( $false, $true ); }
    };
    [System.IO.File]::WriteAllBytes( $path, ( $encoding.GetPreamble() + $encoding.GetBytes( $file.InnerText ) ) );
}