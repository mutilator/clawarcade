char _sDisplayIncomingCommand[_numChars]; // an array to store the received data
char _lastDisplayMessage[_numChars]; //led message
unsigned long _waitForDisplayAckTimestamp = 0; //time we sent last led message
byte _waitForDisplayAckCount = 0; //retry counter

/*

displayController Comms

*/

// Do request to send data
void notifyDisplayControllerMessage()
{
    _waitForDisplayAckTimestamp = millis();
    displayController.print(RTS);
    displayController.flush();
}

//Send a message to the displayController.port, queues message and waits for CTS response
void sendDisplayControllerMessage(char message[])
{
    notifyDisplayControllerMessage();
    strncpy(_lastDisplayMessage, message, strlen(message)+1);
    
}

//Write data to displayController.port, terminate with close
void sendDisplayControllerData(char message[])
{
    displayController.print(message);
    displayController.print(CS);
}


void handleDisplaySerialCommands()
{
    //if we sent a message but didn't receive an ACK then send again
    if (_waitForDisplayAckTimestamp && millis() - _waitForDisplayAckTimestamp > 300)
    {
        _waitForDisplayAckCount++;
        notifyDisplayControllerMessage();
    }

    if (_waitForDisplayAckCount > 5) //give up after 5 attempts
    {
        _waitForDisplayAckTimestamp = 0;
        _waitForDisplayAckCount = 0;
    }

    static byte sidx = 0; //displayController.cursor
    static unsigned long startTime = 0; //memory placeholder

    startTime = millis();

    //if we have data, read it
    while (displayController.available()) //burn through data waiting for start byte
    {
        char thisChar = displayController.read();
        if (thisChar == RTS) //if the other end wants to send data, tell them it's OK
        {
            displayController.print(CTS);
            displayController.flush();

            //reset the index
            sidx = 0;
            while (millis() - startTime < 500) //wait up to 300ms for next byte
            {
                if (!displayController.available())
                    continue;

                startTime = millis(); //update received timestamp, allows slow data to come in (manually typing)
                thisChar = displayController.read();

                if (thisChar == RTS) //extra rts, burn it off
                        continue;
                        
                if (thisChar == CS)
                {
                    while (displayController.available() > 0) //burns the buffer
                        displayController.read();

                    _sDisplayIncomingCommand[sidx] = '\0'; //terminate string

                    int eventid = 0;
                    char data[10];
                    debugString("Term: ");
                    debugLine(_sDisplayIncomingCommand);

                    // example: 108 1
                    handleTerminalCommand(_sDisplayIncomingCommand);

                    break;
                } else {
                    //save our byte
                    _sDisplayIncomingCommand[sidx] = thisChar;
                    sidx++;
                    //prevent overlfow and reset to our last byte
                    if (sidx >= _numChars) {
                        sidx = _numChars - 1;
                    }
                }
            }
            //we either processed data from a successful command or the command timed out
        }
        else if (thisChar == CTS)
        {
            _waitForDisplayAckTimestamp = 0;
            _waitForDisplayAckCount = 0;

            sendDisplayControllerData(_lastDisplayMessage);
        }
    }
}

/*

End display comms

*/