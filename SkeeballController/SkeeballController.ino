#include "Defines.h"
#include <Wire.h>
#include "DigitalWriteFast.h"

HardwareSerial &shooterController = Serial1;
HardwareSerial &displayController = Serial2;
HardwareSerial &wifiController = Serial3;


bool _isDebugMode = false;

byte curMsgId = 0;

const char RTS = '{'; //request to send data
const char CS = '}'; // complete send data
const char CTS = '!'; //clear to send data

int _maxSensorRechecks = 20000; //how many times to check the 5/6 score sensors for a double trigger

const bool _scoreActivated = LOW; //Pin status when ball passes in front of it
int _sensorActivationDelay = 500; //how long to wait before sensing another activation

bool _checkScoringSensor1 = false;
bool _checkScoringSensor2 = false;
bool _checkScoringSensor3 = false;
bool _checkScoringSensor4 = false;
bool _checkScoringSensor5 = false;
bool _checkScoringSensor6 = false;
bool _checkScoringSensor7 = false;
bool _checkScoringSensor8 = false;

const byte _numChars = 64; //size of comms buffers
const char _commandDelimiter = '\n';

int _currentControllerMode = CONTROLLER_MODE_ONLINE;
int _releaseWaitDuration = 100; //how long after release sensor is tripped should we keep ball release open

byte _localMaxBalls = 9; //how many balls is a single player
byte _ballsReleased = 0; //how many balls do we initially release for local mode? this is used to activate the ball release to get to max balls
int _localModeBallTrackerCount = 0; //used to track how many balls were thrown
long _localModeCurrentScore = 0; //players score
bool _ballReleaseActive = false;
unsigned long _ballReleaseActivatedTime = 0; //when ball release is activated
int _ballReleaseDelay = 0; //how long to hold the release open
unsigned long _timestampScoringEnabled = 0;
int _scoreEnableWaitTime = 2000;
int _ballStopTriggerDuration = 50;
bool _ballReleasedRemotely = false;


byte _ledSlotControllerId = 0x10;

void setup() {
    Wire.begin(); //begin as master
    Serial.begin(115200);
    shooterController.begin(115200);
    displayController.begin(115200);
    wifiController.begin(115200);
    initScoring();
    initExternalButtons();
    
    pinMode(PIN_BALL_RETURN_STOP, OUTPUT);
    pinMode(PIN_LIGHTS, OUTPUT);
    digitalWrite(PIN_LIGHTS, HIGH);
}

void loop() {
   
    checkScoreSensors();
    checkBallRelease();
    checkExternalButtons();
    handleTerminalSerialCommands();
    handleShooterSerialCommands();
    handleDisplaySerialCommands();
    handleWiFiSerialCommands(); 
    
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
    static char argument7[_numChars]= {0}; //holds the setting
    static char argument8[_numChars]= {0}; //holds the setting
    static char argument9[_numChars]= {0}; //holds the setting

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
    sscanf(incomingData, "%s %s %s %s %s %s %s %s %s %s %s", sequence, command, argument, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9);



    /*

    */
    if (strcmp(command,"debug") == 0) { //get debug info
        
        sprintf(outputData, "%i %i", _ballsReleased, _localModeBallTrackerCount);
        sendFormattedResponse(EVENT_INFO, sequence, outputData);
    } else if (strcmp(command,"dbg") == 0) { //enable debug output

        _isDebugMode = strcmp(argument,"1") == 0;
        sendFormattedResponse(EVENT_INFO, sequence, argument);

    } else if (strcmp(command,"ping") == 0) { //pinging

        sendFormattedResponse(EVENT_PONG, sequence, argument);

    } else if (strcmp(command,"cm") == 0) { //pinging
        int mode = atoi(argument);
        if (_currentControllerMode == mode)
        {
            sendFormattedResponse(EVENT_PONG, sequence, argument);
            return;
        }

        changeGameMode(mode);

        sendFormattedResponse(EVENT_PONG, sequence, argument);

    } else if (strcmp(command, "sc") == 0) { //set score sensor
        
        int scoreSlot = atoi(argument);
        int isEnabled = atoi(argument2) == 1?HIGH:LOW;

        setScoring(scoreSlot, isEnabled);

        sprintf(outputData, "%i", scoreSlot);
        sendFormattedResponse(EVENT_INFO, sequence, outputData);

    } else if ((strcmp(command,"ah") == 0) || // auto home
              (strcmp(command,"sh") == 0)  || // set home
              (strcmp(command,"sa") == 0)  || // set acceleration
              (strcmp(command,"ss") == 0) ||  // set speed
              (strcmp(command,"sl") == 0) ||  // set limits
              (strcmp(command,"mt") == 0) ||  // move to
              (strcmp(command,"tr") == 0) ||  // turn right
              (strcmp(command,"tl") == 0) ||  // turn left
              (strcmp(command,"l") == 0) ||   // move left
              (strcmp(command,"r") == 0) ||   // move right
              (strcmp(command,"gl") == 0) ||  // get location
              (strcmp(command,"ws") == 0))    // wheel speed
    {

        sendShooterControllerMessage(incomingData); // send full command to shooter, response is relayed

    } else if (strcmp(command, "sls") == 0) // score led show
    {
        int slot = atoi(argument);
        int r = atoi(argument2);
        int g = atoi(argument3);
        int b = atoi(argument4);
        
        Wire.beginTransmission(0x10);
        Wire.write(0xFE); //header start
        Wire.write(0x01); //command
        Wire.write(0x01); //header end
        Wire.write(slot);
        Wire.write(r);
        Wire.write(g);
        Wire.write(b);
        Wire.endTransmission();
        
        sendFormattedResponse(EVENT_INFO, sequence, argument);
    } else if (strcmp(command, "slss") == 0) // score led show strobe
    {
        
        
        int slot = atoi(argument);
        int r = atoi(argument2);
        int g = atoi(argument3);
        int b = atoi(argument4);

        int r2 = atoi(argument5);
        int g2 = atoi(argument6);
        int b2 = atoi(argument7);

        int sc = atoi(argument8);
        int sd = atoi(argument9);
        
        Wire.beginTransmission(0x10);
        Wire.write(0xFE); //header start
        Wire.write(0x04); //command
        Wire.write(0x01); //header end
        Wire.write(slot);
        Wire.write(r);
        Wire.write(g);
        Wire.write(b);
        Wire.write(r2);
        Wire.write(g2);
        Wire.write(b2);
        Wire.write(sc);
        Wire.write(sd);
        Wire.endTransmission();
        
        sendFormattedResponse(EVENT_INFO, sequence, argument);
    } else if (strcmp(command, "s") == 0) // shoot the ball
    {

        int releaseTime = atoi(argument);
        _releaseWaitDuration = atoi(argument2);
        _ballReleasedRemotely = true;
        releaseBall(argument); //release 2 seconds, release wait duration takes over after the release sensor is tripped
        sendFormattedResponse(EVENT_INFO, sequence, argument);
    } else if (strcmp(command, "br") == 0)
    {
        releaseBall(atoi(argument));
        sendFormattedResponse(EVENT_INFO, sequence, argument);
    } else if (strcmp(command, "bst") == 0)
    {
        _ballStopTriggerDuration = atoi(argument);
        sendFormattedResponse(EVENT_INFO, sequence, argument);
    } else if (strcmp(command, "lights") == 0)
    {
        digitalWrite(PIN_LIGHTS, atoi(argument));
        sendFormattedResponse(EVENT_INFO, sequence, argument);
    } else if (strcmp(command, "ml") == 0)
    {
        _maxSensorRechecks = atoi(argument);
    } else if (strcmp(command, "d") == 0)
    {
         sscanf(incomingData, "%s %s %[^\n]", sequence, command, outputData);
        sendDisplayControllerMessage(outputData);
        sendFormattedResponse(EVENT_INFO, sequence, outputData);
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
        int possibleEvent = atoi(command);
        if (possibleEvent)
        {
            //return command from controll is in format ctrl_seq event sent_seq data
            //ctrl_seq is unique for the controller
            //sent_seq is the sequence passed to the controller
            sscanf(incomingData, "%s %s %s %[^\n]", argument2, command, sequence, argument);
            sendFormattedResponse(possibleEvent, sequence, argument);
            return;
        }
        //always send an acknowledgement that it processed a command, even if nothing fired, it means we cleared the command buffer
        //this is in an else because each function needs to send it's own ack BEFORE it executes functions
        //prevents the command ack from triggering after events occur because of the action
        //e.g. press Down sends a down event immediately which has to come after command ack
        sendFormattedResponse(EVENT_INFO, sequence, "");
    }


}

void changeGameMode(int mode)
{
    _currentControllerMode = mode;
    switch (_currentControllerMode)
    {
        case CONTROLLER_MODE_LOCAL:
            startLocalGame();
            break;
        case CONTROLLER_MODE_ONLINE:
            sendDisplayControllerMessage("Online");
            setScoring(9, false);
        break;
    }
}

void startLocalGame()
{
    _localModeCurrentScore = 0;
    _localModeBallTrackerCount = 0;
    sendDisplayControllerMessage("Go!");
    setScoring(9, true);
    releaseBall(5000);
    _ballsReleased = 0;
}
void checkBallRelease()
{
    //delay of -1 means leave it open, otherwise run based on time
    if (_ballReleaseDelay >= 0 && _ballReleaseActivatedTime > 0 && millis() - _ballReleaseActivatedTime >= _ballReleaseDelay)
    {
        releaseBall(0);
    }
}

//delay of 0 turns off release, -1 leaves on until you turn it off, otherwise run for that many ms
void releaseBall(int delayTime)
{
    _ballReleaseDelay = delayTime;
    digitalWrite(PIN_BALL_RETURN_STOP, delayTime != 0?1:0);
    _ballReleaseActivatedTime = delayTime != 0?millis():0;
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

    if (_isDebugMode)
    {
        Serial.print(event);
        Serial.print(":");
        Serial.print(sequence);
        Serial.print(" "); //ack
        Serial.println(response);
    }
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
void debugLong(unsigned long message)
{
    if (_isDebugMode)
    {
        Serial.print(message);
    }
}
void debugLong(long message)
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