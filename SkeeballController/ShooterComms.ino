char _sShooterIncomingCommand[_numChars]; // an array to store the received data
char _lastShooterMessage[_numChars]; //led message
unsigned long _waitForShooterAckTimestamp = 0; //time we sent last led message
byte _waitForShooterAckCount = 0; //retry counter

/*

shooterController Comms

*/

// Do request to send data
void notifyShooterControllerMessage()
{
    _waitForShooterAckTimestamp = millis();
    shooterController.print(RTS);
    shooterController.flush();
}

//Send a message to the shooterController.port, queues message and waits for CTS response
void sendShooterControllerMessage(char message[])
{
    notifyShooterControllerMessage();
    strncpy(_lastShooterMessage, message, strlen(message)+1);
    
}

//Write data to shooterController.port, terminate with close
void sendShooterControllerData(char message[])
{
    shooterController.print(message);
    shooterController.print(CS);
}


void handleShooterSerialCommands()
{
    //if we sent a message but didn't receive an ACK then send again
    if (_waitForShooterAckTimestamp && millis() - _waitForShooterAckTimestamp > 300)
    {
        _waitForShooterAckCount++;
        notifyShooterControllerMessage();
    }

    if (_waitForShooterAckCount > 5) //give up after 5 attempts
    {
        _waitForShooterAckTimestamp = 0;
        _waitForShooterAckCount = 0;
    }

    static byte sidx = 0; //shooterController.cursor
    static unsigned long startTime = 0; //memory placeholder

    startTime = millis();

    //if we have data, read it
    while (shooterController.available()) //burn through data waiting for start byte
    {
        char thisChar = shooterController.read();
        if (thisChar == RTS) //if the other end wants to send data, tell them it's OK
        {
            shooterController.print(CTS);
            shooterController.flush();

            //reset the index
            sidx = 0;
            while (millis() - startTime < 500) //wait up to 300ms for next byte
            {
                if (!shooterController.available())
                    continue;

                startTime = millis(); //update received timestamp, allows slow data to come in (manually typing)
                thisChar = shooterController.read();

                if (thisChar == RTS) //extra rts, burn it off
                        continue;
                        
                if (thisChar == CS)
                {
                    while (shooterController.available() > 0) //burns the buffer
                        shooterController.read();

                    _sShooterIncomingCommand[sidx] = '\0'; //terminate string

                    int eventid = 0;
                    char data[10];
                    debugString("Term: ");
                    debugLine(_sShooterIncomingCommand);

                    // example: 108 1
                    handleTerminalCommand(_sShooterIncomingCommand);

                    break;
                } else {
                    //save our byte
                    _sShooterIncomingCommand[sidx] = thisChar;
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
            _waitForShooterAckTimestamp = 0;
            _waitForShooterAckCount = 0;

            sendShooterControllerData(_lastShooterMessage);
        }
    }
}

/*

End shooter comms

*/