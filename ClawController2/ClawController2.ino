HardwareSerial &clawController = Serial1;
HardwareSerial &conveyorController = Serial2;
HardwareSerial &wifiController = Serial3;


const int _PINConveyorSensor = 13;

bool _isDebugMode = true;

byte cmdStartGame[] = {0xFE, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0x14, 0x31, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46};

byte cmdMoveBackward[] = {0xFE, 0xFE, 0x00, 0x01, 0x01, 0xFF, 0x0C, 0x32, 0x00, 0x90, 0x01, 0x07};
byte cmdMoveForward[] = {0xFE, 0xFD, 0x00, 0x01, 0x02, 0xFF, 0x0C, 0x32, 0x01, 0x90, 0x01, 0x08};
byte cmdMoveLeft[] = {0xFE, 0xFC, 0x00, 0x01, 0x03, 0xFF, 0x0C, 0x32, 0x02, 0x90, 0x01, 0x09};
byte cmdMoveRight[] = {0xFE, 0xFB, 0x00, 0x01, 0x04, 0xFF, 0x0C, 0x32, 0x03, 0x90, 0x01, 0x0A};
byte cmdMoveDrop[] = {0xFE, 0xFA, 0x00, 0x01, 0x05, 0xFF, 0x0C, 0x32, 0x04, 0x00, 0x00, 0x42};
byte cmdMoveStop[] = {0xFE, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0x0C, 0x32, 0x05, 0x00, 0x00, 0x43};

byte cmdQueryMachineState[] = {0xFE, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0x09, 0x34, 0x3D};
byte cmdHeartbeatStop[] = {0xFE, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0x09, 0x36, 0x3F};
byte cmdResetMachine[] = {0xFE, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0x09, 0x38, 0x41};
byte cmdReadStatus[] = {0xFE, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0x09, 0x3E, 0x47};

byte curMsgId = 0;

const char RTS = '{'; //request to send data
const char CS = '}'; // complete send data
const char CTS = '!'; //clear to send data


//These are specifically for communication with the motor controller
const byte BELT_MOTOR_COMMAND_CLEAR_SAFE_START = 0x83;
const byte BELT_MOTOR_COMMAND_FORWARD = 0x85;
const byte BELT_MOTOR_COMMAND_REVERSE = 0x86;
const byte BELT_MOTOR_COMMAND_GET_VAR = 0xA1;
const byte BELT_MOTOR_COMMAND_STOP = 0xE0;


unsigned long _timestampOfDropCommand = 0; //when the claw drops we start a time that returns the claw to center and then sends an event that it's ready to go again
int _dropResetDelay = 12000; //how long in millis it takes for the claw to drop and return to the win chute
unsigned long _timestampOfMoveToCenter = 0; //When returning claw to center, this is when it started
int _moveForwardAfterGrabTime = 1500;
int _moveRightAfterGrabTime = 1500;

//CONVEYOR BELT STUFF 
unsigned long _timestampConveyorSensorTripped = 0; //what time the sensor last saw movement
unsigned long _timestampConveyorBeltStart = 0; //when the conveyor belt started running
int _conveyorSensorTripped = HIGH; //used to determine if trip is on a high or low
int _failsafeBeltLimit = 30000; //limit for running conveyor belt
int _conveyorMoveDuration = 0;


int _conveyorBeltRunTime = 0;
int _conveyorFlipperRunTime = 0; //how long to run the flipper in a direction before we call the stop command?
int _conveyorSpeed = 80; //speed in percent of the conveyor belt

int _conveyorSensorTripDelay = 1000; //amount of time to wait before looking for another sensor event
int _gameResetTripDelay = 4000; //amount of time to wait before looking for another sensor event

/*

  EVENTS

*/
const int EVENT_CONVEYOR_TRIPPED = 100; //response when tripped
const int EVENT_PONG = 101; //ping reply
const int EVENT_GAME_RESET = 102; //send a reset event from the machine
const int EVENT_DROPPING_CLAW = 103; //started drop
const int EVENT_DROPPED_CLAW = 104; //finished drop and started recoil
const int EVENT_RECOILED_CLAW = 105; //finished recoil
const int EVENT_RETURNED_HOME = 106; //over win chute
const int EVENT_RETURNED_CENTER = 107; //back in center
const int EVENT_SCORE_SENSOR = 108; //plinko sensor
const int EVENT_CONVEYOR2_TRIPPED = 109; //response when tripped



const int EVENT_INFO = 900; //Event to show when we want to pass info back

bool _heartBeating = true;


const byte _numChars = 64;
const char _commandDelimiter = '\n';
char _incomingCommand[_numChars]; // an array to store the received data from wifi controller
char _sTerminalIncomingCommand[_numChars]; // an array to store the received data
char _lastTerminalMessage[_numChars]; //led message
unsigned long _waitForTerminalAckTimestamp = 0; //time we sent last led message
byte _waitForTerminalAckCount = 0; //retry counter


void setup() {
    Serial.begin(115200);
    clawController.begin(115200);
    wifiController.begin(115200);
    initMachine();
    initConveyor();
    delay(1000);

}

void loop() {
   
    checkConveyorSensor();
    checkConveyorRuntime();
    handleTerminalSerialCommands();
    handleWiFiSerialCommands();
    checkDropReset();
    if (clawController.available())
    {
        debugLine("Recv Data: ");
        while(clawController.available())
            debugByte(clawController.read());
        debugLine("");
    }
}

void initMachine()
{
    sendClawControllerCommand(cmdStartGame);
        
    cmdMoveForward[9] = _moveForwardAfterGrabTime % 256;
    cmdMoveForward[10] = _moveForwardAfterGrabTime / 256;
    sendClawControllerCommand(cmdMoveForward);
    delay(_moveForwardAfterGrabTime);
    cmdMoveRight[9] = _moveRightAfterGrabTime % 256;
    cmdMoveRight[10] = _moveRightAfterGrabTime / 256;
    sendClawControllerCommand(cmdMoveRight);
}
/*

CONVEYOR BELT STUFF

*/

void initConveyor()
{
    //pinMode(_PINConveyorFlipperError, INPUT);
    //conveyor belt 2
    conveyorController.begin(19200); //talk to motor controller

    pinMode(_PINConveyorSensor, INPUT_PULLUP);
}

// read a serial byte (returns -1 if nothing received after the timeout expires)
int readByteBeltController()
{
  char c;
  if(conveyorController.readBytes(&c, 1) == 0){ return -1; }
  return (byte)c;
}

// returns the specified variable as an unsigned integer.
// if the requested variable is signed, the value returned by this function
// should be typecast as an int.
unsigned int getFlipperVariable(unsigned char variableID)
{
  conveyorController.write(BELT_MOTOR_COMMAND_GET_VAR);
  conveyorController.write(variableID);
  int rtn = readByteBeltController() + 256 * readByteBeltController();
  debugString("mtr cntr var: ");
  debugInt(rtn);
  return rtn;
}

void checkConveyorRuntime()
{
    if ((_timestampConveyorBeltStart > 0 && ((millis() - _timestampConveyorBeltStart >= _conveyorBeltRunTime) && (_conveyorBeltRunTime >= 0))) ||
        (millis() - _timestampConveyorBeltStart >= _failsafeBeltLimit))
    {
        conveyorController.write(BELT_MOTOR_COMMAND_CLEAR_SAFE_START);
        conveyorController.write(BELT_MOTOR_COMMAND_STOP);
        _timestampConveyorBeltStart = 0;
        _conveyorBeltRunTime = 0;
    }
}     

void checkConveyorSensor()
{
    static unsigned long curTime = 0; //memory placeholder

    curTime = millis();
    int sensorVal = digitalRead(_PINConveyorSensor);
    if (sensorVal == _conveyorSensorTripped)
    {
        

        if (curTime - _timestampConveyorSensorTripped > _conveyorSensorTripDelay)
            sendEvent(EVENT_CONVEYOR_TRIPPED);

        _timestampConveyorSensorTripped = curTime;
    }

}


void moveConveyorBelt(int runTime)
{
    int baseSpeed = 0;
    if (runTime != 0)
    {
        conveyorController.write(BELT_MOTOR_COMMAND_CLEAR_SAFE_START);
        conveyorController.write(BELT_MOTOR_COMMAND_FORWARD);
        conveyorController.write(baseSpeed);
        conveyorController.write(_conveyorSpeed);
        _timestampConveyorBeltStart = millis();
        _conveyorBeltRunTime = runTime;
    } else {
        conveyorController.write(BELT_MOTOR_COMMAND_CLEAR_SAFE_START);
        conveyorController.write(BELT_MOTOR_COMMAND_FORWARD);
        conveyorController.write(baseSpeed);
        conveyorController.write(_conveyorSpeed);
        _timestampConveyorBeltStart = 0;
        _conveyorBeltRunTime = 0;
    }
}

/*

END CONVEYOR STUFF

*/

void checkDropReset() 
{
    //Check if the claw is over the chute
    if (_timestampOfDropCommand > 0 && millis() - _timestampOfDropCommand >= _dropResetDelay)
    {
        sendClawControllerCommand(cmdStartGame);
        
        cmdMoveForward[9] = _moveForwardAfterGrabTime % 256;
        cmdMoveForward[10] = _moveForwardAfterGrabTime / 256;
        sendClawControllerCommand(cmdMoveForward);
        delay(_moveForwardAfterGrabTime);
        cmdMoveRight[9] = _moveRightAfterGrabTime % 256;
        cmdMoveRight[10] = _moveRightAfterGrabTime / 256;
        sendClawControllerCommand(cmdMoveRight);
        _timestampOfMoveToCenter = millis();
        _timestampOfDropCommand = 0;
    }

    // check if claw is now centered
    if (_timestampOfMoveToCenter > 0 && millis() - _timestampOfMoveToCenter >= _moveForwardAfterGrabTime)
    {
        
        sendFormattedResponse(EVENT_RETURNED_CENTER, "0", "");
        _timestampOfMoveToCenter = 0;
    }
    
}


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


void notifyTerminalControllerMessage()
{
    _waitForTerminalAckTimestamp = millis();
    Serial.print(RTS);
    Serial.flush();
}

void sendTerminalControllerMessage(char message[])
{
    notifyTerminalControllerMessage();
    strncpy(_lastTerminalMessage, message, strlen(message)+1);
    
}

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

void handleTerminalCommand(char incomingData[])
{
    static char outputData[100];
    static char sequence[10]= {0}; //holds the command
    static char command[_numChars]= {0}; //holds the command
    static char argument[_numChars]= {0}; //holds the axis
    static char argument2[_numChars]= {0}; //holds the setting
    static char argument3[_numChars]= {0}; //holds the setting
    static char argument4[_numChars]= {0}; //holds the setting
    static char argument5[_numChars]= {0}; //holds the setting
    static char argument6[_numChars]= {0}; //holds the setting

    //clear old values
    memset(outputData, 0, sizeof(outputData));
    /*
    memset(sequence, 0, sizeof(sequence));
    memset(command, 0, sizeof(command));
    memset(argument, 0, sizeof(argument));
    memset(argument2, 0, sizeof(argument2));
    memset(argument3, 0, sizeof(argument3));
    memset(argument4, 0, sizeof(argument4));
    memset(argument5, 0, sizeof(argument5));
    memset(argument6, 0, sizeof(argument6));
    */

    //simplistic approach
    sscanf(_incomingCommand, "%s %s %s %s %s %s %s %s", sequence, command, argument, argument2, argument3, argument4, argument5, argument6);


    /*

    */
    if (strcmp(command,"start") == 0) { //restart machine
        sendFormattedResponse(EVENT_INFO, sequence, "");
        sendClawControllerCommand(cmdStartGame);

    } else if (strcmp(command,"f") == 0) { //forward

        sendFormattedResponse(EVENT_INFO, sequence, "");
        int time = atoi(argument); //time to move
        cmdMoveForward[9] = time % 256;
        cmdMoveForward[10] = time / 256;
        sendClawControllerCommand(cmdMoveForward);
        

    } else if (strcmp(command,"b") == 0) { //back

        sendFormattedResponse(EVENT_INFO, sequence, "");
        int time = atoi(argument); //time to move
        cmdMoveBackward[9] = time % 256;;
        cmdMoveBackward[10] = time / 256;
        sendClawControllerCommand(cmdMoveBackward);

    } else if (strcmp(command,"r") == 0) { //right
        
        sendFormattedResponse(EVENT_INFO, sequence, "");
        int time = atoi(argument); //time to move
        cmdMoveRight[9] = time % 256;;
        cmdMoveRight[10] = time / 256;
        sendClawControllerCommand(cmdMoveRight);

    } else if (strcmp(command,"l") == 0) { //left

        sendFormattedResponse(EVENT_INFO, sequence, "");
        int time = atoi(argument); //time to move
        cmdMoveLeft[9] = time % 256;;
        cmdMoveLeft[10] = time / 256;
        sendClawControllerCommand(cmdMoveLeft);

    } else if (strcmp(command,"d") == 0) { //drop

        sendFormattedResponse(EVENT_INFO, sequence, "");
        sendClawControllerCommand(cmdMoveDrop);
        _timestampOfDropCommand = millis();

    } else if (strcmp(command,"s") == 0) { //drop

        sendFormattedResponse(EVENT_INFO, sequence, "");
        sendClawControllerCommand(cmdMoveStop);

    } else if (strcmp(command,"sh") == 0) { //heatbeat cycle
        if (_heartBeating)
        {
            sendClawControllerCommand(cmdHeartbeatStop);
        } else {
            //sendClawControllerCommand(cmdMoveStop, cmdSizeMove);
        }
        _heartBeating = !_heartBeating;
        sendFormattedResponse(EVENT_INFO, sequence, "");
    } else if (strcmp(command,"query") == 0) { //query

        sendFormattedResponse(EVENT_INFO, sequence, "");
        sendClawControllerCommand(cmdQueryMachineState);

    } else if (strcmp(command,"status") == 0) { //status
        sendFormattedResponse(EVENT_INFO, sequence, "");
        sendClawControllerCommand(cmdReadStatus);

    } else if (strcmp(command,"reset") == 0) { //reset
        sendFormattedResponse(EVENT_INFO, sequence, "");
        sendClawControllerCommand(cmdResetMachine);

    } else if (strcmp(command,"ping") == 0) { //pinging

        sendFormattedResponse(EVENT_PONG, sequence, argument);

    }  else if (strcmp(command, "sfs") == 0) { //set failsafes
        
        int type = atoi(argument);
        int value = atoi(argument2);

        switch (type)
        {
            case 2:
                _failsafeBeltLimit = value; //limit for running conveyor belt
                break;
        }

        sprintf(outputData, "%i %i", type, value);
        sendFormattedResponse(EVENT_INFO, argument, outputData);

    } else if (strcmp(command, "gfs") == 0) { //get failsafes
        
        int type = atoi(argument);
        int value = 0;

        switch (type)
        {
            case 2:
                value = _failsafeBeltLimit; //limit for running conveyor belt
                break;
        }

        sprintf(outputData, "%i %i", type, value);
        sendFormattedResponse(EVENT_INFO, argument, outputData);

    } else if (strcmp(command,"srt") == 0) { //set reset times

        sendFormattedResponse(EVENT_INFO, sequence, "");
        _moveForwardAfterGrabTime = atoi(argument);
        _moveRightAfterGrabTime = atoi(argument2);

    } else if (strcmp(command,"belt") == 0) { //move belt for # milliseconds

        sendFormattedResponse(EVENT_INFO, sequence, "");

        int val = atoi(argument);
        moveConveyorBelt(val);

    }  else if (strcmp(command,"bs") == 0) { //set belt speed in percent

        sendFormattedResponse(EVENT_INFO, sequence, "");

        _conveyorSpeed = atoi(argument);

    } else {

        //always send an acknowledgement that it processed a command, even if nothing fired, it means we cleared the command buffer
        //this is in an else because each function needs to send it's own ack BEFORE it executes functions
        //prevents the command ack from triggering after events occur because of the action
        //e.g. press Down sends a down event immediately which has to come after command ack
        sendFormattedResponse(EVENT_INFO, sequence, "");
    }


}

// Generates event text and sends it to the connected client
void sendEvent(int event)
{
    if (event > 0)
    {
        sendFormattedResponse(event, "0", "");
    }
}

void sendFormattedResponse(int event, char sequence[], char response[])
{
    wifiController.print(event);
    wifiController.print(":");
    wifiController.print(sequence);
    wifiController.print(" "); //ack
    wifiController.println(response);
}

void sendClawControllerCommand(byte command[])
{
    curMsgId++;
    //change message ID
    command[1] = curMsgId;
    command[4] = 0xFF - curMsgId;

    //recalculate checksum
    int checksum = 0;
    for(int i = 6; i < command[6]-1; i++)
        checksum = checksum + command[i];

    command[command[6]-1] = checksum % 100;

    debugLine("Sending Command:");
    for(int i = 0; i < command[6]; i++)
    {
        debugByte(command[i]);
        clawController.write(command[i]);
        clawController.flush();
    }
    debugLine("");
}



void debugLine(char* message)
{
    if (_isDebugMode)
    {
        Serial.println(message);
    }
}
void debugString(char* message)
{
    if (_isDebugMode)
    {
        Serial.print(message);
    }
}

void debugInt(int message)
{
    if (_isDebugMode)
    {
        Serial.print(message);
    }
}

void debugByte(byte message)
{
    if (_isDebugMode)
    {
        Serial.print(message, HEX);
        Serial.print(", ");
    }
}