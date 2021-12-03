char _sTerminalIncomingCommand[_numChars]; // an array to store the received data
char _lastTerminalMessage[_numChars]; //led message
unsigned long _waitForTerminalAckTimestamp = 0; //time we sent last led message
byte _waitForTerminalAckCount = 0; //retry counter

/*

Serial/USB Comms

*/

// Do request to send data
void notifyTerminalControllerMessage()
{
    _waitForTerminalAckTimestamp = millis();
    Serial.print(RTS);
    Serial.flush();
}

//Send a message to the serial port, queues message and waits for CTS response
void sendTerminalControllerMessage(char message[])
{
    notifyTerminalControllerMessage();
    strncpy(_lastTerminalMessage, message, strlen(message)+1);
    
}

//Write data to serial port, terminate with close
void sendTerminalControllerData(char message[])
{
    Serial.print(message);
    Serial.print(CS);
}


void handleTerminalSerialCommands()
{
    //if we sent a message but didn't receive an ACK then send again
    if (_waitForTerminalAckTimestamp && millis() - _waitForTerminalAckTimestamp > 300)
    {
        _waitForTerminalAckCount++;
        notifyTerminalControllerMessage();
    }

    if (_waitForTerminalAckCount > 5) //give up after 5 attempts
    {
        _waitForTerminalAckTimestamp = 0;
        _waitForTerminalAckCount = 0;
    }

    static byte sidx = 0; //serial cursor
    static unsigned long startTime = 0; //memory placeholder

    startTime = millis();

    //if we have data, read it
    while (Serial.available()) //burn through data waiting for start byte
    {
        char thisChar = Serial.read();
        if (thisChar == RTS) //if the other end wants to send data, tell them it's OK
        {
            Serial.print(CTS);
            Serial.flush();

            //reset the index
            sidx = 0;
            while (millis() - startTime < 500) //wait up to 300ms for next byte
            {
                if (!Serial.available())
                    continue;

                startTime = millis(); //update received timestamp, allows slow data to come in (manually typing)
                thisChar = Serial.read();

                if (thisChar == RTS) //extra rts, burn it off
                        continue; 

                if (thisChar == CS)
                {
                    while (Serial.available() > 0) //burns the buffer
                        Serial.read();

                    _sTerminalIncomingCommand[sidx] = '\0'; //terminate string

                    int eventid = 0;
                    char data[10];
                    debugString("Term: ");
                    debugLine(_sTerminalIncomingCommand);

                    // example: 108 1
                    handleTerminalCommand(_sTerminalIncomingCommand);

                    break;
                } else {
                    //save our byte
                    _sTerminalIncomingCommand[sidx] = thisChar;
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
            _waitForTerminalAckTimestamp = 0;
            _waitForTerminalAckCount = 0;

            sendTerminalControllerData(_lastTerminalMessage);
        }
    }
}

/*

End serial/USB comms

*/