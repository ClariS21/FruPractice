$client = [System.Net.Sockets.TcpClient]::new()
try {
    $client.Connect('127.0.0.1', 10512)
    $writer = [System.IO.StreamWriter]::new($client.GetStream(), [System.Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    $writer.WriteLine('#hello|FruPractice|test')
    $writer.WriteLine('20|40000001|Oracle of Darkness|9D4A|Ultimate Relativity|E0000000||10.000|100.00|100.00|0.00|3.14')
    $writer.WriteLine('#bye')
    $writer.Dispose()
}
finally {
    $client.Dispose()
}
