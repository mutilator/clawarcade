char _incomingCommand[_numChars]; // an array to store the received data from wifi controller

/*

WiFi passthrough

*/

void handleWiFiSerialCommands()
{
    static byte idx = 0; //index for socket cursor, if multi connection this will b0rk?
    while (wifiController.available())
    {
        // read the bytes incoming from the client:
        char thisChar = wifiController.read();
        if (thisChar == _commandDelimiter)
        {
            _incomingCommand[idx] = '\0'; //terminate string
            handleTerminalCommand(_incomingCommand);
            idx = 0;
        } else {
            if (thisChar != '\r') //ignore CR
            {
                //save our byte
                _incomingCommand[idx] = thisChar;
                idx++;
                //prevent overlfow and reset to our last byte
                if (idx >= _numChars) {
                    idx = _numChars - 1;
                }
            }
        }
    }
}