#include <SPI.h>
#include <Ethernet.h>
#include <EthernetClient.h>
#include <EthernetServer.h>
#include <SoftwareSerial.h>

SoftwareSerial conveyorController(6, 5);
HardwareSerial &ledController = Serial3;
HardwareSerial &plinkoController = Serial2;

bool _isDebugMode = false; //if true, output stuff to serial

/*
  PIN ASSIGNMENTS
*/
const int _PINMoveLeft = 46;
const int _PINMoveRight = 42;
const int _PINMoveForward = 44;
const int _PINMoveBackward = 40;
const int _PINMoveDown = 36;
const int _PINMoveUp = 34;

const int _PINClawSolenoid = 38;

const int _PINLimitLeft = 43;
const int _PINLimitRight = 37;
const int _PINLimitForward = 41;
const int _PINLimitBackward = 39;
const int _PINLimitDown = 47;
const int _PINLimitUp = 45;

const int _PINLightsWhite = 2;

const int _PINConveyorBelt = 3;
const int _PINConveyorSensor = 8;
const int _PINConveyorSensor2 = 11;
const int _PINBlackLight = 9;
const int _PINClawPower = 12;

const int _PINGameReset = 53;

const int _PINLed = 13;

const int _PINStickMoveLeft = 22;
const int _PINStickMoveRight = 23;
const int _PINStickMoveForward = 24;
const int _PINStickMoveBackward = 25;
const int _PINStickMoveDown = 26;

//const int _PINConveyorFlipperError = 7;
//_PINConveyorFlipperSerial PORT 18 & 19 = Serial1 = conveyorController


//High/Low settings for what determines on and off state
//const int RELAYPINON = LOW; //INVERTED BOARD
//const int RELAYPINOFF = HIGH;

const int RELAYPINON = HIGH;
const int RELAYPINOFF = LOW;

const int LIMITON = LOW; //Pulldown
const int LIMITOFF = HIGH;

/*

    EEPROM LOCATIONS

*/
const int _memHomeLocation = 0;//where are we storing the home location

bool _doWiggle = false; //whether we wiggle when performing drop
int _wiggleTime = 80; //amount of time to move each direction during the wiggle

int _conveyorSensorTripDelay = 1000; //amount of time to wait before looking for another sensor event
int _gameResetTripDelay = 4000; //amount of time to wait before looking for another sensor event
bool _recoilLimitOverride = false; //set this to make the motor keep tension on the claw temporarily, failsafe is still in effect here
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


const int EVENT_LIMIT_LEFT = 200; //hit a limit
const int EVENT_LIMIT_RIGHT = 201; //hit a limit
const int EVENT_LIMIT_FORWARD = 202; //hit a limit
const int EVENT_LIMIT_BACKWARD = 203; //hit a limit
const int EVENT_LIMIT_UP = 204; //hit a limit
const int EVENT_LIMIT_DOWN = 205; //hit a limit

const int EVENT_FLIPPER_FORWARD = 206; //hit a limit
const int EVENT_FLIPPER_HOME = 207; //hit a limit
const int EVENT_FLIPPER_ERROR = 208; //hit a limit

const int EVENT_FAILSAFE_LEFT = 300; //hit a FAILSAFE
const int EVENT_FAILSAFE_RIGHT = 301; //hit a FAILSAFE
const int EVENT_FAILSAFE_FORWARD = 302; //hit a FAILSAFE
const int EVENT_FAILSAFE_BACKWARD = 303; //hit a FAILSAFE
const int EVENT_FAILSAFE_UP = 304; //hit a FAILSAFE
const int EVENT_FAILSAFE_DOWN = 305; //hit a FAILSAFE
const int EVENT_FAILSAFE_CLAW = 306; //hit a FAILSAFE
const int EVENT_FAILSAFE_FLIPPER = 307; //hit a FAILSAFE


const int EVENT_INFO = 900; //Event to show when we want to pass info back



/*

  STATE MANAGEMENT

*/
const byte STATE_RUNNING = 0;
const byte STATE_CHECK_HOMING_L = 1;
const byte STATE_CHECK_HOMING_B = 2;
const byte STATE_CHECK_CENTERING = 3;
const byte STATE_CHECK_DROP_TENSION = 4;
const byte STATE_CHECK_DROP_RECOIL = 5;
const byte STATE_CHECK_RUNCHUTE_LEFT = 6;
const byte STATE_CHECK_RUNCHUTE_BACK = 7;
const byte STATE_CHECK_RUNCHUTE_RIGHT = 8;
const byte STATE_CHECK_RUNCHUTE_FORWARD = 9;
const byte STATE_CLAW_STARTUP = 10;
const byte STATE_CHECK_STARTUP_RECOIL = 11;
const byte STATE_CHECK_STARTUP_RIGHT = 12;
const byte STATE_CHECK_STARTUP_FORWARD = 13;
const byte STATE_CHECK_HOMING = 14;
const byte STATE_FAILSAFE = 15;

/*

    GAME mode

*/
const byte GAMEMODE_CLAW = 0; //normal mode
const byte GAMEMODE_TARGET = 1; //start over home, drop to grab stuff, keep claw closed, move over drop location, open cloaw, return home


byte _gameMode = GAMEMODE_CLAW;



/*

    HOME LOCATIONS

*/
const byte HOME_LOCATION_FL = 0; //Set home locaton front left - default
const byte HOME_LOCATION_FR = 1; //Set home locaton front right
const byte HOME_LOCATION_BL = 2; //Set home locaton back left
const byte HOME_LOCATION_BR = 3; //Set home locaton back right

byte _homeLocation = HOME_LOCATION_FL; //Set home locaton front left - default


//Storing times
unsigned long _timestampConveyorSensorTripped = 0; //what time the sensor last saw movement
unsigned long _timestampConveyor2SensorTripped = 0; //what time the sensor last saw movement
unsigned long _timestampGameResetTrippedTime = 0; //Time the reset button was pressed
unsigned long _timestampRunLeft = 0; //When did we start running right to left
unsigned long _timestampRunBackward = 0; //When did we start running back to front
unsigned long _timestampConveyorBeltStart = 0; //when the conveyor belt started running
unsigned long _timestampRunCenter = 0; //timestamp set when we start running gantry to the center
unsigned long _timestampConveyorBeltStart2 = 0; //second conveyor start
unsigned long _timestampConveyorFlipperStart = 0; //when did flipper start movement

unsigned long _timestampMotorMoveUp = 0; //motor movement failsafe stamp
unsigned long _timestampMotorMoveDown = 0; //motor movement failsafe stamp
unsigned long _timestampMotorMoveLeft = 0; //motor movement failsafe stamp
unsigned long _timestampMotorMoveRight = 0; //motor movement failsafe stamp
unsigned long _timestampMotorMoveForward = 0; //motor movement failsafe stamp
unsigned long _timestampMotorMoveBackward = 0; //motor movement failsafe stamp
unsigned long _timestampMotorMoveFlipper = 0; //motor movement failsafe stamp
unsigned long _timestampClawClosed = 0;
unsigned long _timestampBeltRun = 0;




int _timespanRunWidth = 0; //amount of time it took to run from right side to left side
int _timespanRunDepth = 0; //amount of time it took to run from back to front
int _halfTimespanRunWidth = 0; //half time, where the center
int _halfTimespanRunDepth = 0; //half time, where the center
int _runToCenterDurationDepth = 0;
int _runToCenterDurationWidth = 0;
bool _enableReturnToChute = true; //after drop, return to chute?

//boot state of the motor, only set in stopMotor* and runMotor* functions
bool _isMotorRunningUp = false;
bool _isMotorRunningDown = false;
bool _isMotorRunningLeft = false;
bool _isMotorRunningRight = false;
bool _isMotorRunningForward = false;
bool _isMotorRunningBackward = false;
bool _isClawClosed = false;
bool _moveFromJoystickBackward = false;
bool _moveFromJoystickForward = false;
bool _moveFromJoystickLeft = false;
bool _moveFromJoystickRight = false;

int _conveyorSensorTripped = HIGH; //used to determine if trip is on a high or low
int _failsafeMotorLimit = 15000; //second limit for how long a motor can move before it should hit a limit
int _failsafeClawOpened = 15000; //limit for how long the claw can be closed
int _failsafeBeltLimit = 30000; //limit for running conveyor belt
int _failsafeFlipperLimit = 4000; //limit for flipper in ONE direction
int _failsafeMaxResets = 4; //maximum number of times we can hit a failsafe before giving up completely
int _failsafeCurrentResets = 0; //how many times we've hit a failsave and not recovered


/*

  NETWORK VARIABLES - used for performing network commands

*/

//How long should we run in this direction
int _clawRemoteMoveDurationForward = 0;
int _clawRemoteMoveDurationBackward = 0;
int _clawRemoteMoveDurationLeft = 0;
int _clawRemoteMoveDurationRight = 0;
int _clawRemoteMoveDurationDrop = 0;
int _clawRemoteMoveDurationDown = 0;
int _clawRemoteMoveDurationUp = 0;
int _conveyorMoveDuration = 0;
int _conveyorMoveDuration2 = 0;


//When did the move begin
unsigned long _clawRemoteMoveStartTimeForward = 0;
unsigned long _clawRemoteMoveStartTimeBackward = 0;
unsigned long _clawRemoteMoveStartTimeLeft = 0;
unsigned long _clawRemoteMoveStartTimeRight = 0;
unsigned long _clawRemoteMoveStartTimeDrop = 0;
unsigned long _clawRemoteMoveStartTimeDown = 0;
unsigned long _clawRemoteMoveStartTimeUp = 0;

//used for looking at a readable string when passing commands to moveFromRemote()
const byte CLAW_FORWARD = 1;
const byte CLAW_BACKWARD = 2;
const byte CLAW_LEFT = 3;
const byte CLAW_RIGHT = 4;
const byte CLAW_DROP = 5; //drop command, used as a one shot drop procedure
const byte CLAW_RECOIL = 6; //recoil command, used as a one shot recoil
const byte CLAW_UP = 7; //pull claw up, used for small movements
const byte CLAW_DOWN = 8; //pull claw down, used for small movements

const byte FLIPPER_STOPPED = 0;
const byte FLIPPER_FORWARD = 1;
const byte FLIPPER_BACKWARD = 2;

//These are specifically for communication with the motor controller
const byte FLIPPER_MOTOR_COMMAND_CLEAR_SAFE_START = 0x83;
const byte FLIPPER_MOTOR_COMMAND_FORWARD = 0x85;
const byte FLIPPER_MOTOR_COMMAND_REVERSE = 0x86;
const byte FLIPPER_MOTOR_COMMAND_GET_VAR = 0xA1;
const byte FLIPPER_MOTOR_COMMAND_STOP = 0xE0;


//CONVEYOR BELT STUFF 
int _conveyorBeltRunTime = 0;
int _conveyorFlipperRunTime = 0; //how long to run the flipper in a direction before we call the stop command?
byte _conveyorFlipperStatus = 0;

int _conveyorBeltRunTime2 = 0;
int _conveyorSpeed2 = 80; //speed in percent of the conveyor belt

int _flipperSpeed = 75;
bool _autoDropTargeting = false; //If in targeting mode, setting this TRUE causes the claw to drop when over the home location
bool _allowTargetingMoves = true; //When in tatrgeting mode, allow the player to move anywhere in the play area during their turn


const char RTS = '{'; //request to send data
const char CS = '}'; //complete send data
const char CTS = '!'; //clear to send data

char _lastPlinkoMessage[40]; //plinko message
unsigned long _waitForAckTimestamp = 0; //time we sent last plinko message
byte _waitForAckCount = 0; //retry counter

char _lastLedMessage[40]; //led message
unsigned long _waitForLedAckTimestamp = 0; //time we sent last led message
byte _waitForLedAckCount = 0; //retry counter

bool _hasQueuedCommand = false;
bool _needsSecondaryInit = true; //true if we need to set GPIO during very first loop() rather than setup()


byte _lastState = STATE_RUNNING;
byte _currentState = STATE_RUNNING;

/*
      SERVER DEFINITIONS

*/
// the media access control (ethernet hardware) address for the shield:
byte mac[] = { 0xDE, 0xAD, 0xBE, 0xEF, 0xFE, 0xE1 };
//the IP address for the shield:
IPAddress ip (10, 1, 2, 196 );

/// etherserver on 23
const int _clientCount = 4; //how many clients do we allow to connect
EthernetServer _server(23);
EthernetClient _clients[_clientCount]; //list of connections


//MISC for handling commands
// buffers for receiving and sending data
const byte _numChars = 64;
char _incomingCommand[_numChars]; // an array to store the received data
char _sPlinkoIncomingCommand[_numChars]; // an array to store the received data
char _sLedIncomingCommand[_numChars]; // an array to store the received data
char _commandDelimiter = '\n';


void setup() {
    Serial.begin(115200);
    ledController.begin(115200);
    plinkoController.begin(250000);

    initGeneral();
    initEthernet();

    //init server
    _server.begin();
    delay(1000);
}

void loop() {
    if (_needsSecondaryInit)
    {
        initMovement();
        initLights();
        initConveyor();
        startupMachine();
        _needsSecondaryInit = false;
    }

    handleTelnetConnectors();
    handleLedSerialCommands();
    handlePlinkoSerialCommands();
    checkConveyorSensor();
    checkBeltRuntime();
    checkBelt2Runtime();
    
    checkMovements();
    checkGameReset();
    handleJoystick();
    handleFlipper();

    checkLimits();
    checkStates();
    
}


void initEthernet()
{
    Ethernet.begin(mac, ip);
}

/**
 *
 * INITIALIZATION FUNCTIONS
 *
 */
void initGeneral()
{
    pinMode(_PINGameReset, INPUT_PULLUP);
    pinMode(_PINLed, OUTPUT);
    digitalWrite(_PINLed, LOW);
}

void initMovement()
{
    //Drive relays for each direction
    pinMode(_PINMoveLeft, OUTPUT);
    pinMode(_PINMoveRight, OUTPUT);
    pinMode(_PINMoveForward, OUTPUT);
    pinMode(_PINMoveBackward, OUTPUT);
    pinMode(_PINMoveDown, OUTPUT);
    pinMode(_PINMoveUp, OUTPUT);
    pinMode(_PINClawSolenoid, OUTPUT);
    pinMode(_PINClawPower, OUTPUT);

    analogWrite(_PINClawPower, 20);


    //Limit switch inputs
    pinMode(_PINLimitLeft, INPUT_PULLUP);
    pinMode(_PINLimitRight, INPUT_PULLUP);
    pinMode(_PINLimitForward, INPUT_PULLUP);
    pinMode(_PINLimitBackward, INPUT_PULLUP);
    pinMode(_PINLimitDown, INPUT_PULLUP);
    pinMode(_PINLimitUp, INPUT_PULLUP);

    //force low
    digitalWrite(_PINMoveLeft, RELAYPINOFF);
    digitalWrite(_PINMoveRight, RELAYPINOFF);
    digitalWrite(_PINMoveForward, RELAYPINOFF);
    digitalWrite(_PINMoveBackward, RELAYPINOFF);
    digitalWrite(_PINMoveDown, RELAYPINOFF);
    digitalWrite(_PINMoveUp, RELAYPINOFF);
    digitalWrite(_PINClawSolenoid, RELAYPINOFF);

    //Setup console controls
    pinMode(_PINStickMoveLeft, INPUT_PULLUP);
    pinMode(_PINStickMoveRight, INPUT_PULLUP);
    pinMode(_PINStickMoveForward, INPUT_PULLUP);
    pinMode(_PINStickMoveBackward, INPUT_PULLUP);
    pinMode(_PINStickMoveDown, INPUT_PULLUP);
    

}



void initLights()
{
    pinMode(_PINLightsWhite, OUTPUT);
    digitalWrite(_PINLightsWhite, RELAYPINON); //turns lights on
}

void initConveyor()
{
    pinMode(_PINConveyorBelt, OUTPUT);
    //pinMode(_PINConveyorFlipperError, INPUT);
    //conveyor belt 2
    conveyorController.begin(19200); //talk to motor controller

    pinMode(_PINConveyorSensor, INPUT_PULLUP);
    pinMode(_PINConveyorSensor2, INPUT_PULLUP);

    digitalWrite(_PINConveyorBelt, RELAYPINOFF); //relay
}

void handleJoystick()
{
    //ignore console when 
    if (_currentState != STATE_RUNNING)
        return;

    int val = digitalRead(_PINStickMoveDown);
    if (val == LOW)
    {
        if ((_gameMode == GAMEMODE_CLAW) || 
            (_gameMode == GAMEMODE_TARGET && !_isClawClosed)) //when in target mode we're only allowed to drop if the claw isnt closed
            dropClawProcedure();
        else if (_gameMode == GAMEMODE_TARGET && _isClawClosed)
        {
            openClaw();
            delay(1000);
            if (_doWiggle)
                    wiggleClaw();
            delay(1000);
            returnToWinChute();
        }
        return;
    }
    
    if (_gameMode == GAMEMODE_TARGET && !_isClawClosed) //don't allow joystick to move when it's over the chute, we need to drop & grab stuff first
        return;

    val = digitalRead(_PINStickMoveBackward);
    if (val == LOW)
    {
        _moveFromJoystickBackward = true;
        runMotorBackward(false);
    } else if (_moveFromJoystickBackward) {
        _moveFromJoystickBackward = false;
        stopMotorBackward();
    }

    val = digitalRead(_PINStickMoveForward);
    if (val == LOW)
    {
        _moveFromJoystickForward = true;
        runMotorForward(false);
    } else if (_moveFromJoystickForward) {
        _moveFromJoystickForward = false;
        stopMotorForward();
    }

    val = digitalRead(_PINStickMoveLeft);
    if (val == LOW)
    {
        _moveFromJoystickLeft = true;
        runMotorLeft(false);
    } else if (_moveFromJoystickLeft) {
        _moveFromJoystickLeft = false;
        stopMotorLeft();
    }

    val = digitalRead(_PINStickMoveRight);
    if (val == LOW)
    {
        _moveFromJoystickRight = true;
        runMotorRight(false);
    } else if (_moveFromJoystickRight) {
        _moveFromJoystickRight = false;
        stopMotorRight();
    }

}

void checkLimits()
{
    static unsigned long curTime = 0;
    curTime = millis();

    if (_isMotorRunningUp && (isLimitUp() || curTime - _timestampMotorMoveUp > _failsafeMotorLimit))
    {
        debugLine("H L U");
        //up and down events require a bit of extra pause when the limit sensor is hit just to make sure it's all the way there
        delay(200); //brief rest for the claw


        if (curTime - _timestampMotorMoveUp > _failsafeMotorLimit)
        {
            stopMotorUp(); //failsafe kills motor regardless of override
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_UP);
            return; //if we hit a failsafe, don't send anymore notifications
        }
        else if (!_recoilLimitOverride) //motor override not in effect
        {
            stopMotorUp();
            sendEvent(EVENT_LIMIT_UP);
        }
    }

    if (_isMotorRunningDown && (isLimitDown() || curTime - _timestampMotorMoveDown > _failsafeMotorLimit))
    {
        debugLine("H L D");
        //up and down events require a bit of extra pause when the limit sensor is hit just to make sure it's all the way there
        delay(200); //brief rest for the claw
        stopMotorDown();
        if (curTime - _timestampMotorMoveDown > _failsafeMotorLimit)
        {
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_DOWN);
            return; //if we hit a failsafe, don't send anymore notifications
        }
        else
            sendEvent(EVENT_LIMIT_DOWN);
    }

    if (_isMotorRunningLeft && (isLimitLeft() || curTime - _timestampMotorMoveLeft > _failsafeMotorLimit))
    {
        debugLine("H L L");
        stopMotorLeft();
        if (curTime - _timestampMotorMoveLeft > _failsafeMotorLimit)
        {
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_LEFT);
            return; //if we hit a failsafe, don't send anymore notifications
        }
        else
            sendEvent(EVENT_LIMIT_LEFT);
    }

    if (_isMotorRunningRight && (isLimitRight() || curTime - _timestampMotorMoveRight > _failsafeMotorLimit))
    {
        debugLine("H L R");
        stopMotorRight();
        if (curTime - _timestampMotorMoveRight > _failsafeMotorLimit)
        {
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_RIGHT);
            return; //if we hit a failsafe, don't send anymore notifications
        }
        else
            sendEvent(EVENT_LIMIT_RIGHT);
    }

    if (_isMotorRunningForward && (isLimitForward() || curTime - _timestampMotorMoveForward > _failsafeMotorLimit))
    {
        debugLine("H L F");
        stopMotorForward();
        if (curTime - _timestampMotorMoveForward > _failsafeMotorLimit)
        {
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_FORWARD);
            return; //if we hit a failsafe, don't send anymore notifications
        }
        else
            sendEvent(EVENT_LIMIT_FORWARD);
    }

    if (_isMotorRunningBackward && (isLimitBackward() || curTime - _timestampMotorMoveBackward > _failsafeMotorLimit))
    {
        debugLine("H L B");
        stopMotorBackward();
        if (curTime - _timestampMotorMoveBackward > _failsafeMotorLimit)
        {
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_BACKWARD);
            return; //if we hit a failsafe, don't send anymore notifications
        }
        else
            sendEvent(EVENT_LIMIT_BACKWARD);
    }

    if (_isClawClosed && (curTime - _timestampClawClosed > _failsafeClawOpened))
    {
        debugLine("C F");
        openClaw();
        if (_gameMode == GAMEMODE_CLAW)
            sendEvent(EVENT_FAILSAFE_CLAW);
        else if (_gameMode == GAMEMODE_TARGET)
        {
            delay(250);
            returnToWinChute();
        }
        return;
    }
}

void checkStates()
{
    switch (_currentState)
    {
        case  STATE_CHECK_RUNCHUTE_LEFT:
            if (!isLimitUp())
            {
                stopMotorLeft();
                changeState(STATE_CHECK_DROP_RECOIL);
            }
        case STATE_CHECK_RUNCHUTE_RIGHT:
            if (!isLimitUp())
            {
                stopMotorRight();
                changeState(STATE_CHECK_DROP_RECOIL);
            }
            break;
    }

    //Now check the current state and perform an action
    switch (_currentState)
    {
        case STATE_CHECK_STARTUP_RECOIL:
            if (isLimitUp())
            {
                stopMotorUp();
                startupMachine();
                debugLine(_currentState);
            }
            else if (!_isMotorRunningUp) //a scenario where the motor was stopped but it's not at the limit
            {
                runMotorUp(false);
            }
            break;
        case STATE_CHECK_STARTUP_RIGHT:
            if (isLimitRight())
            {
                stopMotorRight();
                startupMachine();
                debugLine(_currentState);
            }
            else if (!_isMotorRunningRight) //a scenario where the motor was stopped but it's not at the limit
            {
                runMotorRight(false);
            }

            break;

        case STATE_CHECK_STARTUP_FORWARD:
            if (isLimitForward())
            {
                stopMotorForward();
                startupMachine();
                debugLine(_currentState);
            }
            else if (!_isMotorRunningForward) //a scenario where the motor was stopped but it's not at the limit
            {
                runMotorForward(false);
            }

            break;

        case STATE_CHECK_HOMING_L:
            if (isLimitLeft())
            {
                stopMotorLeft();
                performHoming();
                debugLine(_currentState);
            } else if (!_isMotorRunningLeft) //a scenario where the motor was stopped but it's not at the limit
            {
                runMotorLeft(false);
            }

            break;

        case STATE_CHECK_HOMING_B:
            if (isLimitBackward())
            {
                stopMotorBackward();
                performHoming();
                debugLine(_currentState);
            }
            else if (!_isMotorRunningBackward) //a scenario where the motor was stopped but it's not at the limit
            {
                runMotorBackward(false);
            }

            break;
        case STATE_CHECK_CENTERING:
            {
                unsigned long diffTime = millis() - _timestampRunCenter;

                bool isWidthReached = false; //for left/right movement
                switch (_gameMode)
                {
                    case GAMEMODE_CLAW:
                
                        if (diffTime > _runToCenterDurationWidth)
                        {
                            stopMotorRight();
                            stopMotorLeft();
                            isWidthReached = true;
                        }
                        break;
                    case GAMEMODE_TARGET:
                        if (diffTime > _runToCenterDurationWidth + 250)
                        {
                            stopMotorRight();
                            stopMotorLeft();
                            isWidthReached = true;
                        }
                        break;
                }

                if (diffTime > _runToCenterDurationDepth)
                {
                    stopMotorForward();
                    stopMotorBackward();
                    if (isWidthReached)
                    {
                        _failsafeCurrentResets = 0; //reset the failsafe counter because we properly reset
                        _recoilLimitOverride = false; //disable the limit override, limit check will turn off the motor
                        changeState(STATE_RUNNING);
                        sendEvent(EVENT_RETURNED_CENTER);
                    }
                }
            }
            break;
        case STATE_CHECK_DROP_TENSION:
            if (isLimitDown())
            {
                stopMotorDown();
                delay(300); //brief rest for the claw
                dropClawProcedure();
            }
            else if (!_isMotorRunningDown) //a scenario where the motor was stopped but it's not at the limit
            {
                runMotorDown(false);
            }

            break;
        case STATE_CHECK_DROP_RECOIL:
            if (isLimitUp())
            {
                stopMotorUp();
                dropClawProcedure();
            }
            else if (!_isMotorRunningUp) //a scenario where the motor was stopped but it's not at the limit
            {
                runMotorUp(false);
            }

            break;
        case STATE_CHECK_RUNCHUTE_LEFT:
            if (isLimitLeft())
            {
                stopMotorLeft();
                delay(300); //brief rest for the claw
                returnToWinChute();
            }
            else if (!_isMotorRunningLeft) //a scenario where the motor was stopped but it's not at the limit
            {
                runMotorLeft(false);
            }

            break;
        case STATE_CHECK_RUNCHUTE_RIGHT:
            if (isLimitRight())
            {
                stopMotorRight();
                delay(300); //brief rest for the claw
                returnToWinChute();
            }
            else if (!_isMotorRunningRight) //a scenario where the motor was stopped but it's not at the limit
            {
                runMotorRight(false);
            }
            break;
        case STATE_CHECK_RUNCHUTE_FORWARD:
            if (isLimitForward())
            {
                stopMotorForward();
                delay(300); //brief rest for the claw
                returnToWinChute();
            }
            else if (!_isMotorRunningForward) //a scenario where the motor was stopped but it's not at the limit
            {
                runMotorForward(false);
            }

            break;
        case STATE_CHECK_RUNCHUTE_BACK:
            if (isLimitBackward())
            {
                stopMotorBackward();
                delay(300); //brief rest for the claw
                returnToWinChute();
            }
            else if (!_isMotorRunningBackward) //a scenario where the motor was stopped but it's not at the limit
            {
                runMotorBackward(false);
            }
            break;

    }
}

/**
 *
 * Kicks off startup procedure, recoils claw, runs to the back right corner of the machine then performHoming()
 *
 */
void startupMachine()
{
    _gameMode = 0;
    _homeLocation = HOME_LOCATION_FL;

    
    if (!isLimitUp())
    {
        
        changeState(STATE_CHECK_STARTUP_RECOIL);
        runMotorUp(false);
        return;
    }

    if (!isLimitRight())
    {
        changeState(STATE_CHECK_STARTUP_RIGHT);
        runMotorRight(false);
        return;
    }

    if (!isLimitForward())
    {
        changeState(STATE_CHECK_STARTUP_FORWARD);
        runMotorForward(false);
        return;
    }

    changeState(STATE_CHECK_HOMING);
    performHoming();

}

/**
 *
 * Starting from the back right side of the machine, run to the left, time it, run to the front, time it, divide the time by two, save it, then returnCenterFromChute()
 *
 */
void performHoming()
{
    static unsigned long curTime = 0;
    curTime = millis();

    if (_currentState == STATE_CHECK_HOMING)
    {
        _timestampRunLeft = curTime;
        changeState(STATE_CHECK_HOMING_L);
        if (!isLimitLeft())
        {
            runMotorLeft(false);
            return;
        }
    }

    if (_currentState == STATE_CHECK_HOMING_L)
    {
        _timespanRunWidth = curTime - _timestampRunLeft; //possibly re-use timestamprunleft if memory required
        _timestampRunBackward = curTime;
        changeState(STATE_CHECK_HOMING_B);
        if (!isLimitBackward())
        {
            runMotorBackward(false);
            return;
        }
    }

    if (_currentState == STATE_CHECK_HOMING_B)
    {
        _timespanRunDepth = curTime - _timestampRunBackward; //possibly re-use _timestampRunBackward if memory required

        _halfTimespanRunWidth = _timespanRunWidth / 2;
        _halfTimespanRunDepth = _timespanRunDepth / 2;
        _runToCenterDurationDepth = _halfTimespanRunDepth;
        _runToCenterDurationWidth = _halfTimespanRunWidth;
        returnCenterFromChute();
    }


}

/**
 *
 * Drop claw, recoil, and returnToWinChute()
 *
 */
void dropClawProcedure()
{
    if (_gameMode == GAMEMODE_CLAW)
    {
        if (_currentState == STATE_CHECK_DROP_TENSION)
        {
            changeState(STATE_CHECK_DROP_RECOIL);
            sendEvent(EVENT_DROPPED_CLAW);
            closeClaw();
            runMotorUp(false);
            return;
        }

        else if (_currentState == STATE_CHECK_DROP_RECOIL)
        {
            sendEvent(EVENT_RECOILED_CLAW);
            if (_enableReturnToChute)
                returnToWinChute();
            else
                changeState(STATE_RUNNING);
            return;
        }

        else //start drop if all else fails
        {
            changeState(STATE_CHECK_DROP_TENSION);
            sendEvent(EVENT_DROPPING_CLAW);
            runMotorDown(false);
            return;
        }
    } else if (_gameMode == GAMEMODE_TARGET)
    {
        switch (_currentState)
        {
            case STATE_CHECK_DROP_TENSION:
                closeClaw();
                changeState(STATE_CHECK_DROP_RECOIL);
                sendEvent(EVENT_DROPPED_CLAW);
                runMotorUp(false);
                return;
            case STATE_CHECK_DROP_RECOIL:
                sendEvent(EVENT_RECOILED_CLAW);
                returnCenterFromChute();
                return;
            case STATE_RUNNING:
                changeState(STATE_CHECK_DROP_TENSION);
                sendEvent(EVENT_DROPPING_CLAW);
                runMotorDown(false);
                return;
        }
    }
}

/**
 *
 * run to left & front of machine, open claw, then returnCenterFromChute()
 *
 */
void returnToWinChute()
{
    //ran left/right to limit
    if (_currentState == STATE_CHECK_RUNCHUTE_LEFT || _currentState == STATE_CHECK_RUNCHUTE_RIGHT)
    {
        if (_homeLocation == HOME_LOCATION_BR || _homeLocation == HOME_LOCATION_BL) //run forward (back of machine)
        {
            changeState(STATE_CHECK_RUNCHUTE_FORWARD);
            runMotorForward(false);
        } else {
            changeState(STATE_CHECK_RUNCHUTE_BACK); //run backward (front of machine)
            runMotorBackward(false);
        }
        return;
    }
    else if (_currentState == STATE_CHECK_RUNCHUTE_BACK || _currentState == STATE_CHECK_RUNCHUTE_FORWARD) //over the win chute now
    {
        sendEvent(EVENT_RETURNED_HOME);
        openClaw();

        delay(200);

        if (_doWiggle)
            wiggleClaw(); //blocking wiggle

        delay(250);

        if (_gameMode == GAMEMODE_CLAW)
            returnCenterFromChute();
        else if (_gameMode == GAMEMODE_TARGET)
        {
            stopMotorUp(); //stop recoil
            //grab some more stuff
            _clawRemoteMoveDurationDrop = 0;
            _clawRemoteMoveStartTimeDrop = millis();
            if (_autoDropTargeting)
                dropClawProcedure();
            else
                changeState(STATE_RUNNING);
        }

        return;
    } else
    {
        
        //recoil claw with force when returning home
        _recoilLimitOverride = true;
        runMotorUp(true);
        delay(200);

        if (_homeLocation == HOME_LOCATION_FR || _homeLocation == HOME_LOCATION_BR) //run right
        {
            changeState(STATE_CHECK_RUNCHUTE_RIGHT);
            runMotorRight(false);
        } else
        { // if (_homeLocation == HOME_LOCATION_FL)
            changeState(STATE_CHECK_RUNCHUTE_LEFT);
            runMotorLeft(false);
        }
        return;
    }

}

/**
 *
 * To return to center take the times set from performHoming and then run to the back/right for that amount of time
 *
 */
void returnCenterFromChute()
{
    _timestampRunCenter = millis();
    
    changeState(STATE_CHECK_CENTERING);

    
    //start direction based on the home
    if (_homeLocation == HOME_LOCATION_BL || _homeLocation == HOME_LOCATION_FL)
        runMotorRight(false);
    else
        runMotorLeft(false);
    
    if (_homeLocation == HOME_LOCATION_FL || _homeLocation == HOME_LOCATION_FR)
        runMotorForward(false);
    else
        runMotorBackward(false);
}




/**
 *
 * EVENT HANDLING
 *
 */

void notifyLedControllerMessage()
{
    _waitForLedAckTimestamp = millis();
    ledController.print(RTS);
    ledController.flush();
}

void sendLedControllerMessage(char message[])
{
    notifyLedControllerMessage();
    strncpy(_lastLedMessage, message, strlen(message)+1);
    
}

void sendLedControllerData(char message[])
{
    ledController.print(message);
    ledController.print(CS);
}

void handleLedSerialCommands()
{
    //if we sent a message but didn't receive an ACK then send again
    if (_waitForLedAckTimestamp && millis() - _waitForLedAckTimestamp > 300)
    {
        _waitForLedAckCount++;
        notifyLedControllerMessage();
    }

    if (_waitForLedAckCount > 5) //give up after 5 attempts
    {
        _waitForLedAckTimestamp = 0;
        _waitForLedAckCount = 0;
    }

    static byte sidx = 0; //serial cursor
    static unsigned long startTime = 0; //memory placeholder

    startTime = millis();

    //if we have data, read it
    while (ledController.available()) //burn through data waiting for start byte
    {
        char thisChar = ledController.read();
        if (thisChar == RTS) //if the other end wants to send data, tell them it's OK
        {
            ledController.print(CTS);
            ledController.flush();

            //reset the index
            sidx = 0;
            while (millis() - startTime < 300) //wait up to 300ms for next byte
            {
                if (!ledController.available())
                    continue;

                startTime = millis(); //update received timestamp, allows slow data to come in (manually typing)
                thisChar = ledController.read();
                if (thisChar == RTS)
                    continue;
                if (thisChar == CS)
                {
                    while (ledController.available() > 0) //burns the buffer
                        ledController.read();

                    _sLedIncomingCommand[sidx] = '\0'; //terminate string

                    int eventid = 0;
                    char data[10];
                    debugString("LED: ");
                    debugLine(_sLedIncomingCommand);

                    // example: 108 1
                    broadcastToClients(_sLedIncomingCommand);

                    break;
                } else {
                    //save our byte
                    _sLedIncomingCommand[sidx] = thisChar;
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
            _waitForLedAckTimestamp = 0;
            _waitForLedAckCount = 0;

            sendLedControllerData(_lastLedMessage);
        }
    }
}

void notifyPlinkoControllerMessage()
{
    _waitForAckTimestamp = millis();
    plinkoController.print(RTS);
    plinkoController.flush();
}

void sendPlinkoControllerMessage(char message[])
{
    notifyPlinkoControllerMessage();
    strncpy(_lastPlinkoMessage, message, strlen(message)+1);
    
}

void sendPlinkoControllerData(char message[])
{
    plinkoController.print(message);
    plinkoController.print(CS);
}

//command from plinko is ".command args!"
void handlePlinkoSerialCommands()
{
    //if we sent a message but didn't receive an ACK then send again
    if (_waitForAckTimestamp && millis() - _waitForAckTimestamp > 300)
    {
        _waitForAckCount++;
        notifyPlinkoControllerMessage();
    }

    if (_waitForAckCount > 5) //give up after 5 attempts
    {
        _waitForAckTimestamp = 0;
        _waitForAckCount = 0;
    }
    
    static byte sidx = 0; //serial cursor
    static unsigned long startTime = 0; //memory placeholder

    startTime = millis();

    //if we have data, read it
    while (plinkoController.available()) //burn through data waiting for start byte
    {
        char thisChar = plinkoController.read();
        if (thisChar == RTS)
        {
            plinkoController.print(CTS);
            plinkoController.flush();

            //reset the index
            sidx = 0;

            while (millis() - startTime < 300) //wait up to 300ms for next byte
            {

                if (!plinkoController.available())
                    continue;

                startTime = millis(); //update received timestamp, allows slow data to come in (manually typing)
                thisChar = plinkoController.read();
                if (thisChar == RTS)
                    continue;
                if (thisChar == CS)
                {

                    while (plinkoController.available() > 0) //burns the buffer
                        plinkoController.read();

                    _sPlinkoIncomingCommand[sidx] = '\0'; //terminate string
                    
                    int eventid = 0;
                    char data[10];
                    debugString("Plinko: ");
                    debugLine(_sPlinkoIncomingCommand);
                    sscanf(_sPlinkoIncomingCommand, "%d %s", &eventid, data);

                    // example: 108 1
                    broadcastToClients(eventid, data);

                    break;
                } else {
                    //save our byte
                    _sPlinkoIncomingCommand[sidx] = thisChar;
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
            _waitForAckTimestamp = 0;
            _waitForAckCount = 0;

            sendPlinkoControllerData(_lastPlinkoMessage);
        }
    }
}

/**
 *
 * Checks movement time limits set from remote clients
 *
 */
void checkMovements()
{
    static unsigned long curTime = 0; //memory placeholder

    curTime = millis();

    //if duration is negative, we may have started movign the claw but it requires a stop command to stop it
    //we verify there is also a start time
    //then check if we've waited long enough
    if (_clawRemoteMoveDurationForward >= 0 && _clawRemoteMoveStartTimeForward > 0 && curTime - _clawRemoteMoveStartTimeForward >= _clawRemoteMoveDurationForward)
    {
        stopMotorForward();
        _clawRemoteMoveStartTimeForward = 0;
    }
    if (_clawRemoteMoveDurationBackward >= 0 && _clawRemoteMoveStartTimeBackward > 0 && curTime - _clawRemoteMoveStartTimeBackward >= _clawRemoteMoveDurationBackward)
    {
        stopMotorBackward();
        _clawRemoteMoveStartTimeBackward = 0;
    }
    if (_clawRemoteMoveDurationLeft >= 0 && _clawRemoteMoveStartTimeLeft > 0 && curTime - _clawRemoteMoveStartTimeLeft >= _clawRemoteMoveDurationLeft)
    {
        stopMotorLeft();
        _clawRemoteMoveStartTimeLeft = 0;
    }
    if (_clawRemoteMoveDurationRight >= 0 && _clawRemoteMoveStartTimeRight > 0 && curTime - _clawRemoteMoveStartTimeRight >= _clawRemoteMoveDurationRight)
    {
        stopMotorRight();
        _clawRemoteMoveStartTimeRight = 0;
    }
    if (_clawRemoteMoveDurationDown >= 0 && _clawRemoteMoveStartTimeDown > 0 && curTime - _clawRemoteMoveStartTimeDown >= _clawRemoteMoveDurationDown)
    {
        stopMotorDown();
        _clawRemoteMoveStartTimeDown = 0;
    }
    if (_clawRemoteMoveDurationUp >= 0 && _clawRemoteMoveStartTimeUp > 0 && curTime - _clawRemoteMoveStartTimeUp >= _clawRemoteMoveDurationUp)
    {
        stopMotorUp();
        _clawRemoteMoveStartTimeUp = 0;
    }
}


// read a serial byte (returns -1 if nothing received after the timeout expires)
int readByteFlipper()
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
  conveyorController.write(FLIPPER_MOTOR_COMMAND_GET_VAR);
  conveyorController.write(variableID);
  int rtn = readByteFlipper() + 256 * readByteFlipper();
  debugString("mtr cntr var: ");
  debugInt(rtn);
  return rtn;
}

//Move the flipper 
void moveFlipper(byte direction)
{
    return;
    /*
    _conveyorFlipperStatus = direction;
    int baseSpeed = 0;
    switch (direction)
    {
        case FLIPPER_FORWARD:
            // TODO - do this smarter, need to check if the current limit event is the forward position so we don't have to bother starting the flipper
            
            _timestampConveyorFlipperStart = millis();
            //blindly clearing this error means we are no longer presenting a high error pin but we also may not be able to move because the controller won't let us
            conveyorController.write(FLIPPER_MOTOR_COMMAND_CLEAR_SAFE_START);
            conveyorController.write(FLIPPER_MOTOR_COMMAND_FORWARD);
            conveyorController.write(baseSpeed);
            conveyorController.write(_flipperSpeed);
            break;
        case FLIPPER_BACKWARD:
            // TODO - do this smarter, need to check if the current limit event is the home position so we don't have to bother starting the flipper
            _timestampConveyorFlipperStart = millis();
            conveyorController.write(FLIPPER_MOTOR_COMMAND_CLEAR_SAFE_START);
            conveyorController.write(FLIPPER_MOTOR_COMMAND_REVERSE);
            conveyorController.write(baseSpeed);
            conveyorController.write(_flipperSpeed);
            break;
        default:
            _conveyorFlipperStatus = FLIPPER_STOPPED;
            _timestampConveyorFlipperStart = 0;
            conveyorController.write(FLIPPER_MOTOR_COMMAND_CLEAR_SAFE_START);
            conveyorController.write(FLIPPER_MOTOR_COMMAND_STOP);
            break;
    }
    */
}

void handleFlipper()
{
    return;
/*
    char ERROR_VAR = 0;
    char LIMIT_VAR = 3;
    short BIT_SAFESTART = 1;
    short BIT_FORWARD = 256;
    short BIT_HOME = 128;

    //check flipper state
    
    //if moving forward, check if _PINConveyorFlipperError
        //verify by polling device
        //if hit then send event flipper is forward
    //if moving backward, check if _PINConveyorFlipperError
        //verify by polling device
        //if hit back then send event flipper is back
    //if an error is seen but didnt hit a limit in the direction it was travelling, bug out
    //otherwise check if we're trying to limit how long the flipper moved in any one direction
    int errPin = digitalRead(_PINConveyorFlipperError);
    if (_conveyorFlipperStatus == FLIPPER_FORWARD && errPin == HIGH)
    {
        int output = getFlipperVariable(LIMIT_VAR);
        if (output & BIT_FORWARD == BIT_FORWARD)
        {
            moveFlipper(FLIPPER_STOPPED);
            sendEvent(EVENT_FLIPPER_FORWARD);
        }
    }
    else if (_conveyorFlipperStatus == FLIPPER_BACKWARD && errPin == HIGH)
    {
        int output = getFlipperVariable(LIMIT_VAR);
        if (output & BIT_HOME == BIT_HOME)
        {
            moveFlipper(FLIPPER_STOPPED);
            sendEvent(EVENT_FLIPPER_HOME);
        }
    } else if (_conveyorFlipperStatus != FLIPPER_STOPPED && errPin == HIGH)
    {
        int output = getFlipperVariable(LIMIT_VAR);
        int output2 = getFlipperVariable(ERROR_VAR);
        char outputData[4];
        sprintf(outputData, "%i %i %i", _conveyorFlipperStatus, output, output2);
        broadcastToClients(EVENT_FLIPPER_ERROR, outputData);
        _conveyorFlipperStatus = FLIPPER_STOPPED;
    } else {

        if (_timestampConveyorFlipperStart > 0 && millis() - _timestampConveyorFlipperStart >= _failsafeFlipperLimit)
        {
            moveFlipper(FLIPPER_STOPPED);
            sendEvent(EVENT_FAILSAFE_FLIPPER);
        }

    }
*/
}

void checkBeltRuntime()
{
    if ((_timestampConveyorBeltStart > 0 && ((millis() - _timestampConveyorBeltStart >= _conveyorBeltRunTime) && (_conveyorBeltRunTime >= 0))) ||
        (millis() - _timestampConveyorBeltStart >= _failsafeBeltLimit))
    {
        digitalWrite(_PINConveyorBelt, RELAYPINOFF);
        _timestampConveyorBeltStart = 0;
        _conveyorBeltRunTime = 0;
    }
}

void checkBelt2Runtime()
{
    if ((_timestampConveyorBeltStart2 > 0 && ((millis() - _timestampConveyorBeltStart2 >= _conveyorBeltRunTime2) && (_conveyorBeltRunTime2 >= 0))) ||
        (millis() - _timestampConveyorBeltStart2 >= _failsafeBeltLimit))
    {
        conveyorController.write(FLIPPER_MOTOR_COMMAND_CLEAR_SAFE_START);
        conveyorController.write(FLIPPER_MOTOR_COMMAND_STOP);
        _timestampConveyorBeltStart2 = 0;
        _conveyorBeltRunTime2 = 0;
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

    sensorVal = digitalRead(_PINConveyorSensor2);
    if (sensorVal == _conveyorSensorTripped)
    {
        if (curTime - _timestampConveyor2SensorTripped > _conveyorSensorTripDelay)
            sendEvent(EVENT_CONVEYOR2_TRIPPED);

        _timestampConveyor2SensorTripped = curTime;
    }
}

void checkGameReset()
{
    int sensorVal = digitalRead(_PINGameReset);
    if (sensorVal == LOW)
    {
        //if enough time passed, send event
        if (millis() - _timestampGameResetTrippedTime > _gameResetTripDelay)
            sendEvent(EVENT_GAME_RESET);

        //button is still pressed, keep resetting the time so the timer to next event kicks off from the end of the button hold
        _timestampGameResetTrippedTime = millis();

    }
}

// Generates event text and sends it to the connected client
void sendEvent(int event)
{
    if (event > 0)
    {
        broadcastToClients(event, "");
    }
}

/**
 *
 *  NETWORK COMMUNICATION
 *
 */
void broadcastToClients(int event, char outputData[])
{
    debugInt(event);
    debugString(":0 ");
    debugLine(outputData);

    for (byte i=0; i < _clientCount; i++)
    {
        if (_clients[i] && _clients[i].connected())
            sendFormattedResponse(_clients[i], event, "0", outputData);
    }
}

void broadcastToClients(char outputData[])
{
    debugInt(EVENT_INFO);
    debugString(":0 ");
    debugLine(outputData);

    for (byte i=0; i < _clientCount; i++)
    {
        if (_clients[i] && _clients[i].connected())
            sendFormattedResponse(_clients[i], EVENT_INFO, "0", outputData);
    }
}

void handleTelnetConnectors()
{
    // see if someone said something
    EthernetClient client = _server.accept();

    //check for disconnected clients
    for (byte i=0; i < _clientCount; i++)
    {
        if (_clients[i] && !_clients[i].connected())
            _clients[i].stop();
    }

    //new client connected
    if (client) {
        bool foundSlot = false;
        for (byte i=0; i < _clientCount; i++)
        {
            if (!_clients[i] || !_clients[i].connected()) {
                //add this person to the list of clients
                _clients[i] = client;
                client.flush();
                foundSlot = true;
                break;
            }
        }
        //if there was no slot open, take the first one
        if (!foundSlot)
        {
            _clients[0].flush();
            _clients[0].stop();
            _clients[0] = client;
            client.flush();
        }
    }

    //check for new data from everyone
    for (byte i=0; i < _clientCount; i++)
    {
        //read all data in buffer
        while(_clients[i] && _clients[i].available())
        {
            //handle next byte of data
            handleClientComms(_clients[i]);
        }
    }

}

void handleClientComms(EthernetClient &client)
{
    static byte idx = 0; //index for socket cursor, if multi connection this will b0rk?
    // read the bytes incoming from the client:
    char thisChar = client.read();
    if (thisChar == _commandDelimiter)
    {

        _incomingCommand[idx] = '\0'; //terminate string
        handleTelnetCommand(client);
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

/**
 *
 *
 *
 * NETWORK COMMANDS
 *
 *
 *
 */


void handleTelnetCommand(EthernetClient &client)
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
    if (strcmp(command,"reset") == 0) { //restart machine

        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        _failsafeCurrentResets = 0; //force failsafe counter reset
        
        startupMachine();

    } else if (strcmp(command,"dbg") == 0) { //some debug info

        int val = atoi(argument);
        if (val == 1)
        {
            _isDebugMode = true;
        } else {
            _isDebugMode = false;
        }
        sprintf(outputData, "%i", _isDebugMode);
        sendFormattedResponse(client, EVENT_INFO, sequence, outputData);

    } else if (strcmp(command,"debug") == 0) { //some debug info

        sprintf(outputData, "%i,%i,%i,%i,%i,%i,%i,%s", _currentState, _lastState, _halfTimespanRunWidth, _halfTimespanRunDepth, _wiggleTime, _failsafeMotorLimit, _homeLocation, _lastLedMessage);
        sendFormattedResponse(client, EVENT_INFO, sequence, outputData);

    } else if (strcmp(command,"state") == 0) { //set machine state manually

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int state = atoi(argument);
        changeState(state);

    } else if (strcmp(command,"ps") == 0) { //pin setting

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int pin = atoi(argument);
        int val = atoi(argument2);
        digitalWrite(pin, val);

    } else if (strcmp(command,"pm") == 0) { //pin mode

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int pin = atoi(argument);
        int val = atoi(argument2);
        pinMode(pin, val);

    } else if (strcmp(command,"pr") == 0) { //pin read

        int pin = atoi(argument);

        sprintf(outputData, "%i", digitalRead(pin));
        sendFormattedResponse(client, EVENT_INFO, sequence, outputData);

    } else if (strcmp(command, "sfs") == 0) { //set failsafes
        
        int type = atoi(argument);
        int value = atoi(argument2);

        switch (type)
        {
            case 0:
                _failsafeMotorLimit = value; //second limit for how long a motor can move before it should hit a limit
                break;
            case 1:
                _failsafeClawOpened = value; //limit for how long the claw can be closed
                break;
            case 2:
                _failsafeBeltLimit = value; //limit for running conveyor belt
                break;
            case 3:
                _failsafeFlipperLimit = value; //limit for flipper in ONE direction
                break;
        }

        sprintf(outputData, "%i %i", type, value);
        sendFormattedResponse(client, EVENT_INFO, argument, outputData);

    } else if (strcmp(command, "gfs") == 0) { //get failsafes
        
        int type = atoi(argument);
        int value = 0;

        switch (type)
        {
            case 0:
                value = _failsafeMotorLimit; //second limit for how long a motor can move before it should hit a limit
                break;
            case 1:
                value = _failsafeClawOpened; //limit for how long the claw can be closed
                break;
            case 2:
                value = _failsafeBeltLimit; //limit for running conveyor belt
                break;
            case 3:
                value = _failsafeFlipperLimit; //limit for flipper in ONE direction
                break;
        }

        sprintf(outputData, "%i %i", type, value);
        sendFormattedResponse(client, EVENT_INFO, argument, outputData);

    } else if (strcmp(command,"f") == 0) { //forward

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int duration = atoi(argument);
        moveFromRemote(CLAW_FORWARD, duration);

    } else if (strcmp(command, "b") == 0) { //backward

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int duration = atoi(argument);
        moveFromRemote(CLAW_BACKWARD, duration);

    } else if (strcmp(command, "l") == 0) { //left

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int duration = atoi(argument);
        moveFromRemote(CLAW_LEFT, duration);

    } else if (strcmp(command,"r") == 0) { //right

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int duration = atoi(argument);
        moveFromRemote(CLAW_RIGHT, duration);

    } else if (strcmp(command,"d") == 0) { //drop

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int duration = atoi(argument);
        moveFromRemote(CLAW_DROP, duration);

    } else if (strcmp(command,"s") == 0) { //stop movement

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        //don't do anything if we're not in a mode to accept input
        if (_currentState != STATE_RUNNING)
            return;


        stopMotorLeft();
        stopMotorRight();
        stopMotorForward();
        stopMotorBackward();
        stopMotorUp();
        stopMotorDown();

    } else if (strcmp(command,"u") == 0) { //move claw up

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int duration = atoi(argument);
        moveFromRemote(CLAW_UP, duration);

    } else if (strcmp(command,"dn") == 0) { //move claw down, "d" is taken for drop

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int duration = atoi(argument);
        moveFromRemote(CLAW_DOWN, duration);

    } else if (strcmp(command,"rtnchute") == 0) { //set return to chute

        if (strcmp(argument,"on") == 0)
        {
            _enableReturnToChute = true;
            sendFormattedResponse(client, EVENT_INFO, sequence, "on");
        } else {
            _enableReturnToChute = false;
            sendFormattedResponse(client, EVENT_INFO, sequence, "off");
        }

    } else if (strcmp(command,"belt") == 0) { //move belt for # milliseconds

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int val = atoi(argument);
        moveConveyorBelt(val);

    }  else if (strcmp(command,"belt2") == 0) { //move belt2 for # milliseconds

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int val = atoi(argument);
        moveConveyorBelt2(val);

    } else if (strcmp(command,"belt2speed") == 0) { //set belt speed in percent

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        _conveyorSpeed2 = atoi(argument);

    } else if (strcmp(command,"claw") == 0) { //open or close claw

        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        int val = atoi(argument);
        if (val == 1)
            closeClaw();
        else
            openClaw();

    } else if (strcmp(command,"flip") == 0) { //move flipper a direction

        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        int val = atoi(argument);
        moveFlipper(val);
        

    } else if (strcmp(command,"sflip") == 0) { //move flipper out and back

        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        int val = atoi(argument);
        _flipperSpeed = val;
        

    } else if (strcmp(command,"light") == 0) { //lights on or off

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        if (strcmp(argument,"on") == 0)
        {
            digitalWrite(_PINLightsWhite, RELAYPINON);
        } else {
            digitalWrite(_PINLightsWhite, RELAYPINOFF);
        }

    } else if (strcmp(command,"clap") == 0) { //clap claw

        int times = atoi(argument);
        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        clapClaw(times);

    } else if (strcmp(command,"cl") == 0) { //check limit

        int dir = atoi(argument);
        int isLimit = 0;
        switch (dir)
        {
            case CLAW_BACKWARD:

                if (isLimitBackward())
                    isLimit = 1;
                break;

            case CLAW_FORWARD:

                if (isLimitForward())
                    isLimit = 1;
                break;

            case CLAW_LEFT:

                if (isLimitLeft())
                    isLimit = 1;
                break;

            case CLAW_RIGHT:

                if (isLimitRight())
                    isLimit = 1;
                break;

            case CLAW_DROP:

                if (isLimitDown())
                    isLimit = 1;
                break;

            case CLAW_RECOIL:

                if (isLimitUp())
                    isLimit = 1;
                break;

        }
        sprintf(outputData, "%i", isLimit);
        sendFormattedResponse(client, EVENT_INFO, sequence, outputData);

    } else if (strcmp(command,"mode") == 0) { //set game mode

        _gameMode = atoi(argument);
        switch (_gameMode)
        {
            case GAMEMODE_TARGET:
                returnToWinChute(); //runs to chute and stop
                break;
            default:
                returnToWinChute(); //runs to chute and stop
                break;
        }
        sprintf(outputData, "mode set %i", _gameMode);
        sendFormattedResponse(client, EVENT_INFO, sequence, outputData);

    } else if (strcmp(command,"tm") == 0) { //set targeting movement

        _allowTargetingMoves = atoi(argument)==1;
        sprintf(outputData, "auto drop %i", _allowTargetingMoves);
        sendFormattedResponse(client, EVENT_INFO, sequence, outputData);

    } else if (strcmp(command,"ad") == 0) { //set auto drop 

        _autoDropTargeting = atoi(argument)==1;
        sprintf(outputData, "auto drop %i", _autoDropTargeting);
        sendFormattedResponse(client, EVENT_INFO, sequence, outputData);

    } else if (strcmp(command,"shome") == 0) { //set home location

        _homeLocation = atoi(argument);
        sprintf(outputData, "home set %i", _homeLocation);
        sendFormattedResponse(client, EVENT_INFO, sequence, outputData);

    } else if (strcmp(command,"rhome") == 0) { //run to home location

        returnToWinChute(); //runs to chute and stop
        sendFormattedResponse(client, EVENT_INFO, sequence, "");

    } else if (strcmp(command,"center") == 0) { //custom center

        _runToCenterDurationWidth = atoi(argument);
        _runToCenterDurationDepth = atoi(argument2);
        sprintf(outputData, "%i %i", _runToCenterDurationWidth, _runToCenterDurationDepth);
        sendFormattedResponse(client, EVENT_INFO, sequence, outputData);

    } else if (strcmp(command,"creset") == 0) { //reset custom centering

        _runToCenterDurationWidth = _halfTimespanRunWidth;
        _runToCenterDurationDepth = _halfTimespanRunDepth;

        sprintf(outputData, "%i %i", _runToCenterDurationWidth, _runToCenterDurationDepth);
        sendFormattedResponse(client, EVENT_INFO, sequence, outputData);

    } else if (strcmp(command,"w") == 0) { //wiggle when reaching home

        if (strcmp(argument,"on") == 0)
        {
            _doWiggle = true;
            sendFormattedResponse(client, EVENT_INFO, sequence, "on");
        } else {
            _doWiggle = false;
            sendFormattedResponse(client, EVENT_INFO, sequence, "off");
        }

    } else if (strcmp(command,"wt") == 0) { //wiggle time, how long to move each direction

        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        _wiggleTime = atoi(argument);

    } else if (strcmp(command,"strobe") == 0) { //strobe the lights

        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        sprintf(_lastLedMessage, "s %s %s %s %s %s %s", argument, argument2, argument3, argument4, argument5, argument6);
        sendLedControllerMessage(_lastLedMessage);

    } else if (strcmp(command,"uno") == 0) { //send generic commands to uno

        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        sprintf(_lastLedMessage, "%s %s %s %s %s %s", argument, argument2, argument3, argument4, argument5, argument6);
        sendLedControllerMessage(_lastLedMessage);
    } else if (strcmp(command,"p") == 0)
    {
        int power = atoi(argument);
        analogWrite(_PINClawPower, power);
    } else if (strcmp(command,"plinko") == 0) { //send generic commands to uno

        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        sprintf(outputData, "%s %s %s %s %s %s", argument, argument2, argument3, argument4, argument5, argument6);
        sendPlinkoControllerMessage(outputData);
    } else if (strcmp(command,"ping") == 0) { //pinging

        sendFormattedResponse(client, EVENT_PONG, sequence, argument);

    } else {

        //always send an acknowledgement that it processed a command, even if nothing fired, it means we cleared the command buffer
        //this is in an else because each function needs to send it's own ack BEFORE it executes functions
        //prevents the command ack from triggering after events occur because of the action
        //e.g. press Down sends a down event immediately which has to come after command ack
        sendFormattedResponse(client, EVENT_INFO, sequence, "");
    }
}



void sendFormattedResponse(EthernetClient &client, int event, char sequence[], char response[])
{
    client.print(event);
    client.print(":");
    client.print(sequence);
    client.print(" "); //ack
    client.println(response);
}

/**
 * Run the conveyor belt for the passed number of milliseconds
 * 0 = stop
 * -1 = no limit
 */
void moveConveyorBelt(int runTime)
{
    if (runTime != 0)
    {
        digitalWrite(_PINConveyorBelt, RELAYPINON);
        _timestampConveyorBeltStart = millis();
        _conveyorBeltRunTime = runTime;
    } else {
        digitalWrite(_PINConveyorBelt, RELAYPINOFF);
    }
}

void moveConveyorBelt2(int runTime)
{
    int baseSpeed = 0;
    if (runTime != 0)
    {
        conveyorController.write(FLIPPER_MOTOR_COMMAND_CLEAR_SAFE_START);
        conveyorController.write(FLIPPER_MOTOR_COMMAND_FORWARD);
        conveyorController.write(baseSpeed);
        conveyorController.write(_conveyorSpeed2);
        _timestampConveyorBeltStart2 = millis();
        _conveyorBeltRunTime2 = runTime;
    } else {
        conveyorController.write(FLIPPER_MOTOR_COMMAND_CLEAR_SAFE_START);
        conveyorController.write(FLIPPER_MOTOR_COMMAND_FORWARD);
        conveyorController.write(baseSpeed);
        conveyorController.write(_conveyorSpeed2);
    }
}


/**
 * @brief  Move the motor a direction for a specified duration
 * @note
 * @param  direction: Direction to move
 * @param  duration: Duration to move in ms
 * @retval None
 */
void moveFromRemote(byte direction, int duration)
{
    //don't do anything if we're not in a mode to accept input
    if (_currentState != STATE_RUNNING)
        return;

    //Handle dropping first
    switch (direction)
    {
        case CLAW_DROP:
            if ((_gameMode == GAMEMODE_CLAW) || 
                (_gameMode == GAMEMODE_TARGET && !_isClawClosed)) //when in target mode we're only allowed to drop if the claw isnt closed
            {
                _clawRemoteMoveDurationDrop = duration;
                _clawRemoteMoveStartTimeDrop = millis();
                dropClawProcedure();
            }
            else if (_gameMode == GAMEMODE_TARGET && _isClawClosed)
            {
                openClaw();
                delay(500);

                if (_doWiggle)
                    wiggleClaw();

                delay(500);
                returnToWinChute();
            }
            break;
        case CLAW_DOWN:
            _clawRemoteMoveDurationDown = duration;
            _clawRemoteMoveStartTimeDown = millis();
            runMotorDown(false);
            break;
        case CLAW_UP:
            _clawRemoteMoveDurationUp = duration;
            _clawRemoteMoveStartTimeUp = millis();
            runMotorUp(false);
            break;
    }

    if (_gameMode == GAMEMODE_TARGET && !_isClawClosed && !_allowTargetingMoves) //don't allow joystick to move when it's over the chute, we need to drop & grab stuff first
        return;

    //handle movement now
    switch (direction)
    {
        case CLAW_FORWARD:
            _clawRemoteMoveDurationForward = duration;
            _clawRemoteMoveStartTimeForward = millis();
            runMotorForward(false);
            break;
        case CLAW_BACKWARD:
            _clawRemoteMoveDurationBackward = duration;
            _clawRemoteMoveStartTimeBackward = millis();
            runMotorBackward(false);
            break;
        case CLAW_LEFT:
            _clawRemoteMoveDurationLeft = duration;
            _clawRemoteMoveStartTimeLeft = millis();
            runMotorLeft(false);
            break;
        case CLAW_RIGHT:
            _clawRemoteMoveDurationRight = duration;
            _clawRemoteMoveStartTimeRight = millis();
            runMotorRight(false);
            break;
    }
}

/**
 *
 * Motor & Limit Handling
 *
 */


bool isLimitLeft()
{
    return digitalRead(_PINLimitLeft)==LIMITON;
}

bool isLimitRight()
{
    return digitalRead(_PINLimitRight)==LIMITON;
}

bool isLimitForward()
{
    return digitalRead(_PINLimitForward)==LIMITON;
}

bool isLimitBackward()
{
    return digitalRead(_PINLimitBackward)==LIMITON;
}

bool isLimitDown()
{
    return digitalRead(_PINLimitDown)==LIMITOFF;
}

bool isLimitUp()
{
    return digitalRead(_PINLimitUp)==LIMITON;
}

void runMotorLeft(bool override)
{
    stopMotorRight();
    if (!isLimitLeft() || override)
    {
        _isMotorRunningLeft = true;
        _timestampMotorMoveLeft = millis();
        digitalWrite(_PINMoveLeft, RELAYPINON);
    }
}

void runMotorRight(bool override)
{
    stopMotorLeft();
    if (!isLimitRight() || override)
    {
        _isMotorRunningRight = true;
        _timestampMotorMoveRight = millis();
        digitalWrite(_PINMoveRight, RELAYPINON);
    }
}

void runMotorForward(bool override)
{
    stopMotorBackward();
    if (!isLimitForward() || override)
    {
        _isMotorRunningForward = true;
        _timestampMotorMoveForward = millis();
        digitalWrite(_PINMoveForward, RELAYPINON);
    }
}

void runMotorBackward(bool override)
{
    stopMotorForward();
    if (!isLimitBackward() || override)
    {
        _isMotorRunningBackward = true;
        _timestampMotorMoveBackward = millis();
        digitalWrite(_PINMoveBackward, RELAYPINON);
    }
}

void runMotorDown(bool override)
{
    stopMotorUp();
    if (!isLimitDown() || override)
    {
        _isMotorRunningDown = true;
        _timestampMotorMoveDown = millis();
        digitalWrite(_PINMoveDown, RELAYPINON);
    }
}

void runMotorUp(bool override)
{
    stopMotorDown();
    if (!isLimitUp() || override)
    {
        _isMotorRunningUp = true;
        _timestampMotorMoveUp = millis();
        digitalWrite(_PINMoveUp, RELAYPINON);
    }
}

void stopMotorLeft()
{
    if (!_isMotorRunningLeft) //avoid useless writes
        return;

    _isMotorRunningLeft = false;
    digitalWrite(_PINMoveLeft, RELAYPINOFF);
}

void stopMotorRight()
{
    if (!_isMotorRunningRight) //avoid useless writes
        return;

    _isMotorRunningRight = false;
    digitalWrite(_PINMoveRight, RELAYPINOFF);
}

void stopMotorForward()
{
    if (!_isMotorRunningForward) //avoid useless writes
        return;

    _isMotorRunningForward = false;
    digitalWrite(_PINMoveForward, RELAYPINOFF);
}

void stopMotorBackward()
{
    if (!_isMotorRunningBackward) //avoid useless writes
        return;

    _isMotorRunningBackward = false;
    digitalWrite(_PINMoveBackward, RELAYPINOFF);
}

void stopMotorDown()
{
    _isMotorRunningDown = false;
    digitalWrite(_PINMoveDown, RELAYPINOFF);
}

void stopMotorUp()
{
    _isMotorRunningUp = false;
    digitalWrite(_PINMoveUp, RELAYPINOFF);
}

void debugLine(char message[])
{
    if (_isDebugMode)
    {
        Serial.println(message);
    }
}
void debugString(char message[])
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
        Serial.print(message);
    }
}

void closeClaw()
{
    _timestampClawClosed = millis();
    _isClawClosed = true;
    digitalWrite(_PINClawSolenoid, RELAYPINON);
}

void openClaw()
{
    _timestampClawClosed = 0;
    _isClawClosed = false;
    digitalWrite(_PINClawSolenoid, RELAYPINOFF);
}
void changeState(int newState)
{
    debugString("C S - O: ");
    debugByte(_currentState);
    debugString("; N: ");
    debugInt(newState);

    _lastState = _currentState;
    _currentState = newState;
}
/**
 * @brief  Wiggle the claw
 * @note
 * @retval None
 */
void wiggleClaw()
{
    runMotorRight(false);
    delay(_wiggleTime);
    runMotorLeft(false);
    delay(_wiggleTime);
    runMotorRight(false);
    delay(_wiggleTime);
    runMotorLeft(false);
    delay(_wiggleTime);
    stopMotorLeft();
}

void clapClaw(int times)
{
    for(int i = 0; i < times; i++)
    {
        closeClaw();
        delay(_wiggleTime);
        openClaw();
        delay(_wiggleTime);
    }
}

