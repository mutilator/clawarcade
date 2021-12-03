#include <avr/wdt.h>
#include <Wire.h>
#include "Defines.h"
#include "StepperController.h"
#include "DigitalWriteFast.h"

HardwareSerial &clawController = Serial1;


const int _LR_StepPin = PIN_04;
const int _LR_DirPin = PIN_05;
const int _LR_Enable = PIN_06;
const int _LR_SwPin = PIN_10;
const int _LR_SwPin2 = PIN_16;

const int _PAN_StepPin = PIN_07;
const int _PAN_DirPin = PIN_08;
const int _PAN_Enable = PIN_09;
const int _PAN_SwPin = PIN_14;
const int _PAN_SwPin2 = PIN_15;

StepperController stepperLR(_LR_StepPin, _LR_DirPin, _LR_Enable, _LR_SwPin, _LR_SwPin2);
StepperController stepperPAN(_PAN_StepPin, _PAN_DirPin, _PAN_Enable, _PAN_SwPin, _PAN_SwPin2);


const byte WHEEL_MOTOR_COMMAND_CLEAR_SAFE_START = 0x83;
const byte WHEEL_MOTOR_COMMAND_FORWARD = 0x85;
const byte WHEEL_MOTOR_COMMAND_REVERSE = 0x86;
const byte WHEEL_MOTOR_COMMAND_GET_VAR = 0xA1;
const byte WHEEL_MOTOR_COMMAND_STOP = 0xE0;

const byte WHEEL_MOTOR_LEFT_ID = 1;
const byte WHEEL_MOTOR_RIGHT_ID = 2;

bool _isDebugMode = false;

unsigned short _sequence = 0;


byte curMsgId = 0;

const char RTS = '{'; //request to send data
const char CS = '}'; // complete send data
const char CTS = '!'; //clear to send data



const byte _numChars = 32;
const byte _numArgChars = 8;
const char _commandDelimiter = '\n';
char _incomingCommand[_numChars]; // an array to store the received data from wifi controller
char _sTerminalIncomingCommand[_numChars]; // an array to store the received data
char _lastTerminalMessage[_numChars]; //led message
unsigned long _waitForTerminalAckTimestamp = 0; //time we sent last led message
byte _waitForTerminalAckCount = 0; //retry counter


void setup() {
    Serial.begin(115200);
    Wire.begin();
    clawController.begin(115200);
    stepperLR.setId(1);
    stepperLR.setLimitTriggerState(HIGH);
    stepperLR.setEventLimitHome(eventHitHomeLimit);
    stepperLR.setEventLimitEnd(eventHitEndLimit);
    stepperLR.setEventMoveComplete(eventMoveComplete);
    stepperLR.setAccel(9);
    stepperLR.setMaxSpeed(500);
    stepperLR.disableController(1);

    stepperPAN.setId(2);
    stepperPAN.setLimitTriggerState(LOW);
    stepperPAN.setEventLimitHome(eventHitHomeLimit);
    stepperPAN.setEventLimitEnd(eventHitEndLimit);
    stepperPAN.setEventMoveComplete(eventMoveComplete);
    stepperPAN.setLimits(240l, -240l);
    stepperPAN.setAccel(9);
    stepperPAN.setMaxSpeed(400);
    stepperPAN.disableController(1);
    Serial.println("Starting Up");
    delay(1000);

    sendFormattedResponse(EVENT_STARTUP, "0", "");
}

void loop() {
    wdt_enable(WDTO_8S);
    handleTerminalSerialCommands();
    runSteppers(); 
}

void runSteppers()
{
    stepperLR.step();
    stepperPAN.step();
}

void eventMoveComplete(int stepperId)
{
    static char outputData[20];
    long pos = 0;
    switch (stepperId)
    {
        case 1:
            pos = stepperLR.getPosition();
            stepperLR.disableController(1);
            break;
        case 2:
            pos = stepperPAN.getPosition();
            stepperPAN.disableController(1);
            break;
    }
    sprintf(outputData, "%i %ld", stepperId, pos);
    sendFormattedResponse(EVENT_MOVE_COMPLETE, "0", outputData);
}

void eventHitEndLimit(int stepperId)
{
    sendEvent(stepperId, EVENT_LIMIT_RIGHT);
}
void eventHitHomeLimit(int stepperId)
{
    sendEvent(stepperId, EVENT_LIMIT_LEFT);
}

void setWheelSpeed(int wheelId, int wheelSpeed)
{
    if (wheelSpeed == 0)
    {
        Wire.beginTransmission(wheelId);
        Wire.write(WHEEL_MOTOR_COMMAND_CLEAR_SAFE_START);
        Wire.write(WHEEL_MOTOR_COMMAND_STOP);
        Wire.endTransmission();
        return;
    }
    int baseSpeed = 0;
    int motorDirection = WHEEL_MOTOR_COMMAND_FORWARD;
    if (wheelSpeed < 0)
    {
        wheelSpeed = -1 * wheelSpeed;
        motorDirection = WHEEL_MOTOR_COMMAND_REVERSE;
    }

    Wire.beginTransmission(wheelId);
    Wire.write(WHEEL_MOTOR_COMMAND_CLEAR_SAFE_START);
    Wire.write(motorDirection);
    Wire.write(baseSpeed);
    Wire.write(wheelSpeed);
    Wire.endTransmission();
}
/*
##################################
Serial/USB Comms
##################################
*/

// Do request to send data
void notifyTerminalControllerMessage()
{
    _waitForTerminalAckTimestamp = millis();
    clawController.print(RTS);
    clawController.flush();
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
    clawController.print(_sequence);
    clawController.print(" ");
    clawController.print(message);
    clawController.print(CS);
    _sequence++;
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
    while (clawController.available()) //burn through data waiting for start byte
    {
        char thisChar = clawController.read();
        if (thisChar == RTS) //if the other end wants to send data, tell them it's OK
        {
            clawController.print(CTS);
            clawController.flush();

            //reset the index
            sidx = 0;
            while (millis() - startTime < 500) //wait up to 300ms for next byte
            {
                if (!clawController.available())
                    continue;

                startTime = millis(); //update received timestamp, allows slow data to come in (manually typing)
                thisChar = clawController.read();
                
                if (thisChar == RTS) //extra rts, burn it off
                        continue;

                if (thisChar == CS)
                {
                    while (clawController.available() > 0) //burns the buffer
                        clawController.read();

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
##################################
End serial/USB comms
##################################
*/

void debugStuff()
{
    static char oData[100];
    sprintf(oData, "%i {LR [p: %ld] [s: %ld] [l: %i] } {PAN [p: %ld] [s:%ld] [l: %i] }",
    EVENT_INFO, stepperLR.getPosition(), stepperLR.getStepsTaken(), stepperLR.checkLimitSwitches(),
    stepperPAN.getPosition(), stepperPAN.getStepsTaken(), stepperPAN.checkLimitSwitches()
    );
    sendTerminalControllerMessage(oData);
}


void handleTerminalCommand(char incomingData[])
{
    static char outputData[100];
    static char sequence[10]= {0}; //holds the command
    static char command[_numArgChars]= {0}; //holds the command
    static char argument[_numArgChars]= {0}; //holds the axis
    static char argument2[_numArgChars]= {0}; //holds the setting
    static char argument3[_numArgChars]= {0}; //holds the setting
    static char argument4[_numArgChars]= {0}; //holds the setting
    static char argument5[_numArgChars]= {0}; //holds the setting
    static char argument6[_numArgChars]= {0}; //holds the setting

    //clear old values
    memset(outputData, 0, sizeof(outputData));


    //simplistic approach
    sscanf(incomingData, "%s %s %s %s %s %s %s %s", sequence, command, argument, argument2, argument3, argument4, argument5, argument6);

   /*

    */
    if (strcmp(command,"debug") == 0) { //debug output
        debugStuff();
    } else if (strcmp(command,"dbg") == 0) { //enable debug

        _isDebugMode = strcmp(argument,"1") == 0;
        sendFormattedResponse(EVENT_INFO, sequence, argument);

    } else if (strcmp(command,"ping") == 0) { //pinging

        sendFormattedResponse(EVENT_PONG, sequence, argument);

    }
    
    
    else if (strcmp(command,"sa") == 0) // set acceleration
    {

        int accel = atoi(argument2);
        if (strcmp(argument,"1") == 0)
            stepperLR.setAccel(accel);
        else if (strcmp(argument,"2") == 0)
            stepperPAN.setAccel(accel);
        
        sendFormattedResponse(EVENT_INFO, sequence, argument2);

    } else if (strcmp(command,"ah") == 0) // auto home
    {

        if (strcmp(argument,"1") == 0)
            stepperLR.autoHome();
        else if (strcmp(argument,"2") == 0)
            stepperPAN.autoHome();
        
        sendFormattedResponse(EVENT_HOMING_STARTED, sequence, argument);

    } else if (strcmp(command,"sh") == 0) // set home at current location
    {

        if (strcmp(argument,"1") == 0)
            stepperLR.setHome();
        else if (strcmp(argument,"2") == 0)
            stepperPAN.setHome();
        
        sendFormattedResponse(EVENT_HOMING_COMPLETE, sequence, argument);

    } else if (strcmp(command,"ss") == 0) // stepper speed
    {

        int speed = atoi(argument2);
        if (strcmp(argument,"1") == 0)
            stepperLR.setMaxSpeed(speed);
        else if (strcmp(argument,"2") == 0)
            stepperPAN.setMaxSpeed(speed);
        
        sendFormattedResponse(EVENT_INFO, sequence, argument2);

    } else if (strcmp(command,"mt") == 0) // move to
    {

        int location = atoi(argument2);
        if (strcmp(argument,"1") == 0)
            stepperLR.moveTo(location);
        else if (strcmp(argument,"2") == 0)
            stepperPAN.moveTo(location);

        sendFormattedResponse(EVENT_MOVE_STARTED, sequence, argument);

    } else if (strcmp(command,"gl") == 0) // get location
    {
        long pos = 0;
        if (strcmp(argument,"1") == 0)
            pos = stepperLR.getPosition();
        else if (strcmp(argument,"2") == 0)
            pos = stepperPAN.getPosition();

        sprintf(outputData, "%s %ld", argument, pos);
        sendFormattedResponse(EVENT_POSITION, sequence, outputData);

    } else if (strcmp(command,"sl") == 0) // set upper and lower limits
    {

        int limHigh = atoi(argument2);
        int limLow = atoi(argument3);
        if (strcmp(argument,"1") == 0)
            stepperLR.setLimits(limHigh, limLow);
        else if (strcmp(argument,"2") == 0)
            stepperPAN.setLimits(limHigh, limLow);

        sprintf(outputData, "%i %i", limHigh, limLow);

        sendFormattedResponse(EVENT_INFO, sequence, outputData);
    }
    
    
    else if (strcmp(command,"l") == 0) // move left
    {
        int steps = atoi(argument);
        if (steps == 0)
        {
            stepperLR.stop();
        } else if (steps < 0) //negative steps run to end
        {
            stepperLR.runToEnd();
        } else {
            stepperLR.moveSteps(steps);
        }

        sprintf(outputData, "1 1 %ld %i", stepperLR.getPosition(), steps);
        sendFormattedResponse(EVENT_MOVE_STARTED, sequence, outputData);

    }  else if (strcmp(command,"r") == 0) // move right
    {
        int steps = atoi(argument);
        if (steps == 0)
        {
            stepperLR.stop();
        } else if (steps < 0) //negative steps run to end
        {
            stepperLR.returnHome();
        } else {
            steps = steps * -1;
            stepperLR.moveSteps(steps);
        }

        sprintf(outputData, "1 2 %ld %i", stepperLR.getPosition(), steps);
        sendFormattedResponse(EVENT_MOVE_STARTED, sequence, outputData);

    }
    
    
    else if (strcmp(command,"tl") == 0) // pan left
    {
        int steps = atoi(argument);
        if (steps == 0)
        {
            stepperPAN.stop();
        } else if (steps < 0) //negative steps run to end
        {
            stepperPAN.runToEnd();
        } else {
            stepperPAN.moveSteps(steps);
        }

        sprintf(outputData, "2 1 %ld %i", stepperPAN.getPosition(), steps);
        sendFormattedResponse(EVENT_MOVE_STARTED, sequence, outputData);

    } else if (strcmp(command,"tr") == 0) // pan right
    {
        int steps = atoi(argument) ;
        if (steps == 0)
        {
            stepperPAN.stop();
        } else if (steps < 0) //negative steps run to end
        {
            stepperPAN.returnHome();
        } else {
            steps = steps * -1;
            stepperPAN.moveSteps(steps);
        }

        sprintf(outputData, "2 2 %ld %i", stepperPAN.getPosition(), steps);
        sendFormattedResponse(EVENT_MOVE_STARTED, sequence, outputData);

    } 
    
    
    else if (strcmp(command,"ws") == 0) // wheel speed
    {
        int wheelId = atoi(argument);
        int speed = atoi(argument2);
        setWheelSpeed(wheelId, speed);
        sprintf(outputData, "%i %i", wheelId, speed);
        sendFormattedResponse(EVENT_WHEEL_SPEED, sequence, outputData);

    } else if (strcmp(command,"pm") == 0) // pin mode
    {
        int pin = atoi(argument);
        int mode = atoi(argument2);
        pinMode(pin, mode);
        sendFormattedResponse(EVENT_INFO, sequence, "");
    }



    else if (strcmp(command,"ps") == 0) // pin set
    {
        int pin = atoi(argument);
        int val = atoi(argument2);
        digitalWrite(pin, val);
        sendFormattedResponse(EVENT_INFO, sequence, "");
    }
    else if (strcmp(command,"pr") == 0) // pin read
    {
        int pin = atoi(argument);
        sprintf(outputData, "%i", digitalRead(pin));
        sendFormattedResponse(EVENT_INFO, sequence, outputData);
    }
    else if (strcmp(command,"ar") == 0) // analog read
    {
        int pin = atoi(argument);
        
        sprintf(outputData, "%i", analogRead(pin));
        sendFormattedResponse(EVENT_INFO, sequence, outputData);
    } else {

        //always send an acknowledgement that it processed a command, even if nothing fired, it means we cleared the command buffer
        //this is in an else because each function needs to send it's own ack BEFORE it executes functions
        //prevents the command ack from triggering after events occur because of the action
        //e.g. press Down sends a down event immediately which has to come after command ack
        sendFormattedResponse(EVENT_INFO, sequence, "");
    }


}

// Generates event text and sends it to the connected client
void sendEvent(int steppperId, int event)
{
    static char outputData[5];
    if (event > 0)
    {
        sprintf(outputData, "%i", steppperId);
        sendFormattedResponse(event, "0", outputData);
    }
}

void sendFormattedResponse(int event, char sequence[], char response[])
{
    static char outputData[100];
    sprintf(outputData, "%i %s %s", event, sequence, response);
    sendTerminalControllerMessage(outputData);
}


void debugLine(char* message)
{
    if (_isDebugMode)
    {
        clawController.println(message);
    }
}
void debugString(char* message)
{
    if (_isDebugMode)
    {
        clawController.print(message);
    }
}

void debugInt(int message)
{
    if (_isDebugMode)
    {
        clawController.print(message);
    }
}

void debugByte(byte message)
{
    if (_isDebugMode)
    {
        clawController.print(message, HEX);
        clawController.print(", ");
    }
}