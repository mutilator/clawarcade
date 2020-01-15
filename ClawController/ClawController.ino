#include <SPI.h>
#include <Ethernet.h>
#include <EthernetClient.h>
#include <EthernetServer.h>


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

const int _PINClawPower = 38;

const int _PINLimitLeft = 43;
const int _PINLimitRight = 37;
const int _PINLimitForward = 41;
const int _PINLimitBackward = 39;
const int _PINLimitDown = 47;
const int _PINLimitUp = 45;

const int _PINLightsWhite = 2;

const int _PINConveyorBelt = 3;
const int _PINConveyorFlipper = 4;
const int _PINConveyorSensor = 14;

const int _PINGameReset = 15;

const int _PINLed = 13;

//High/Low settings for what determines on and off state
//const int RELAYPINON = LOW; //INVERTED BOARD
//const int RELAYPINOFF = HIGH;

const int RELAYPINON = HIGH; 
const int RELAYPINOFF = LOW;

const int LIMITON = LOW; //Pulldown
const int LIMITOFF = HIGH;

bool _doWiggle = false; //whether we wiggle when performing drop
int _wiggleTime = 300; //amount of time to move each direction during the wiggle

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

const int EVENT_LIMIT_LEFT = 200; //hit a limit
const int EVENT_LIMIT_RIGHT = 201; //hit a limit
const int EVENT_LIMIT_FORWARD = 202; //hit a limit
const int EVENT_LIMIT_BACKWARD = 203; //hit a limit
const int EVENT_LIMIT_UP = 204; //hit a limit
const int EVENT_LIMIT_DOWN = 205; //hit a limit

const int EVENT_FAILSAFE_LEFT = 300; //hit a FAILSAFE
const int EVENT_FAILSAFE_RIGHT = 301; //hit a FAILSAFE
const int EVENT_FAILSAFE_FORWARD = 302; //hit a FAILSAFE
const int EVENT_FAILSAFE_BACKWARD = 303; //hit a FAILSAFE
const int EVENT_FAILSAFE_UP = 304; //hit a FAILSAFE
const int EVENT_FAILSAFE_DOWN = 305; //hit a FAILSAFE
const int EVENT_FAILSAFE_CLAW = 306; //hit a FAILSAFE

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
const byte STATE_CLAW_STARTUP = 8;
const byte STATE_CHECK_STARTUP_RECOIL = 9;
const byte STATE_CHECK_STARTUP_RIGHT = 10;
const byte STATE_CHECK_STARTUP_FORWARD = 11;
const byte STATE_CHECK_HOMING = 12;
const byte STATE_FAILSAFE = 13;


//Storing times
unsigned long _timestampConveyorSensorTripped = 0; //what time the sensor last saw movement
unsigned long _timestampGameResetTrippedTime = 0; //Time the reset button was pressed
unsigned long _timestampRunLeft = 0; //When did we start running right to left
unsigned long _timestampRunBackward = 0; //When did we start running back to front
unsigned long _timestampConveyorBeltStart = 0; //when the conveyor belt started running
unsigned long _timestampRunCenter = 0; //timestamp set when we start running gantry to the center
unsigned long _timestampConveyorFlipperStart = 0; //when did flipper start movement

unsigned long _timestampMotorMoveUp = 0; //motor movement failsafe stamp
unsigned long _timestampMotorMoveDown = 0; //motor movement failsafe stamp
unsigned long _timestampMotorMoveLeft = 0; //motor movement failsafe stamp
unsigned long _timestampMotorMoveRight = 0; //motor movement failsafe stamp
unsigned long _timestampMotorMoveForward = 0; //motor movement failsafe stamp
unsigned long _timestampMotorMoveBackward = 0; //motor movement failsafe stamp
unsigned long _timestampClawClosed = 0;


int _timespanRunWidth = 0; //amount of time it took to run from right side to left side
int _timespanRunDepth = 0; //amount of time it took to run from back to front
int _halfTimespanRunWidth = 0; //half time, where the center 
int _halfTimespanRunDepth = 0; //half time, where the center
int _runToCenterDurationDepth = 0;
int _runToCenterDurationWidth = 0;

//boot state of the motor, only set in stopMotor* and runMotor* functions
bool _isMotorRunningUp = false;
bool _isMotorRunningDown = false;
bool _isMotorRunningLeft = false;
bool _isMotorRunningRight = false;
bool _isMotorRunningForward = false;
bool _isMotorRunningBackward = false;
bool _isClawClosed = false;


int _failsafeMotorLimit = 10000; //second limit for how long a motor can move before it should hit a limit
int _failsafeClawOpened = 10000; //limit for how long the claw can be closed

//CONVEYOR BELT STUFF - network determines how long to run but everything else is internal with the process
int _conveyorBeltRunTime = 0;
int _conveyorFlipperRunTime = 3000;

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

bool _hasQueuedCommand = false; //flag for speed if we have a command queued to go out
char _queuedCommand[100]; //probly too big

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
char _sincomingCommand[_numChars]; // an array to store the received data
char _commandDelimiter = '\n';


void setup() {
    Serial.begin(115200);

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
    handleSerialCommands();
    checkConveyorSensor();
    checkBeltRuntime();
    checkFlipperRuntime();
    checkMovements();
    checkGameReset();

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
    pinMode(_PINClawPower, OUTPUT);


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
    digitalWrite(_PINClawPower, RELAYPINOFF);

}

void initLights()
{
    pinMode(_PINLightsWhite, OUTPUT);
    digitalWrite(_PINLightsWhite, RELAYPINON); //turns lights on
}

void initConveyor()
{
    pinMode(_PINConveyorBelt, OUTPUT);
    pinMode(_PINConveyorFlipper, OUTPUT);

    pinMode(_PINConveyorSensor, INPUT);

    digitalWrite(_PINConveyorBelt, RELAYPINOFF); //relay
    digitalWrite(_PINConveyorFlipper, LOW); //talk to digispark
}


/**
 * 
 * Claw functionality
 * 
 */

void checkLimits()
{
    static unsigned long curTime = 0;
    curTime = millis();

    if (_isMotorRunningUp && (isLimitUp() || curTime - _timestampMotorMoveUp > _failsafeMotorLimit))
    {
        debugLine("Hit Limit Up");
        //up and down events require a bit of extra pause when the limit sensor is hit just to make sure it's all the way there
        delay(200); //brief rest for the claw
        

        if (curTime - _timestampMotorMoveUp > _failsafeMotorLimit)
        {
            stopMotorUp(); //failsafe kills motor regardless of override
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_UP);
        }
        else if (!_recoilLimitOverride) //motor override not in effect
        {
            stopMotorUp();
            sendEvent(EVENT_LIMIT_UP);
        }
    }
    if (_isMotorRunningDown && (isLimitDown() || curTime - _timestampMotorMoveDown > _failsafeMotorLimit))
    {
        debugLine("Hit Limit Down");
        //up and down events require a bit of extra pause when the limit sensor is hit just to make sure it's all the way there
        delay(200); //brief rest for the claw
        stopMotorDown();
        if (curTime - _timestampMotorMoveDown > _failsafeMotorLimit)
        {
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_DOWN);
        }
        else
            sendEvent(EVENT_LIMIT_DOWN);
    }
    if (_isMotorRunningLeft && (isLimitLeft() || curTime - _timestampMotorMoveLeft > _failsafeMotorLimit))
    {
        debugLine("Hit Limit Left");
        stopMotorLeft();
        if (curTime - _timestampMotorMoveLeft > _failsafeMotorLimit)
        {
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_LEFT);
        }
        else
            sendEvent(EVENT_LIMIT_LEFT);
    }
    if (_isMotorRunningRight && (isLimitRight() || curTime - _timestampMotorMoveRight > _failsafeMotorLimit))
    {
        debugLine("Hit Limit Right");
        stopMotorRight();
        if (curTime - _timestampMotorMoveRight > _failsafeMotorLimit)
        {
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_RIGHT);
        }
        else
            sendEvent(EVENT_LIMIT_RIGHT);
    }
    if (_isMotorRunningForward && (isLimitForward() || curTime - _timestampMotorMoveForward > _failsafeMotorLimit))
    {
        debugLine("Hit Limit Forward");
        stopMotorForward();
        if (curTime - _timestampMotorMoveForward > _failsafeMotorLimit)
        {
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_FORWARD);
        }
        else
            sendEvent(EVENT_LIMIT_FORWARD);
    }
    if (_isMotorRunningBackward && (isLimitBackward() || curTime - _timestampMotorMoveBackward > _failsafeMotorLimit))
    {
        debugLine("Hit Limit Backward");
        stopMotorBackward();
        if (curTime - _timestampMotorMoveBackward > _failsafeMotorLimit)
        {
            changeState(STATE_FAILSAFE);
            sendEvent(EVENT_FAILSAFE_BACKWARD);
        }
        else
            sendEvent(EVENT_LIMIT_BACKWARD);
    }
    if (_isClawClosed && (curTime - _timestampClawClosed > _failsafeClawOpened))
    {
        debugLine("Hit claw open failsafe");
        openClaw();
        sendEvent(EVENT_FAILSAFE_CLAW);
    }
}

void checkStates()
{
    //There seems to be some oddness with retracting the claw and it thinking it's completely recoiled
    //This check is in place to restart the recoil procedure if it's in one of the recoil/return home phases but the limit switch isnt hit
    switch (_currentState)
    {
        case STATE_RUNNING:
            /*
            //This code didnt work as expected because as the tension is released the switch is untriggered and causes this to loop
            if (_lastState == STATE_CHECK_CENTERING && !isLimitUp())
            {
                
                //runMotorUp(); //runs the motor up, limit switch checks will stop it, stays in the running state while it's recoiling so it accepts commands
                //also keeps the claw tight

            }
            */
            break;
        case STATE_CHECK_RUNCHUTE_LEFT:
            if (!isLimitUp())
            {
                stopMotorLeft();
                changeState(STATE_CHECK_DROP_RECOIL); //change state back to recoil, code below will start the motor pulling the claw back up
            }
            break;
    }

    //Now check the current state and perform an action
    switch (_currentState)
    {
        case STATE_CHECK_STARTUP_RECOIL:
            if (isLimitUp())
            {
                debugLine("hit recoil");
                stopMotorUp();
                startupMachine();
                debugString("New State: ");
                debugLine(_currentState);
            }
            else if (!_isMotorRunningUp) //a scenario where the motor was stopped but it's not at the limit
            {
                debugLine("Startup recoil but motor not running");
                runMotorUp(false);
            }
            break;
        case STATE_CHECK_STARTUP_RIGHT:
            if (isLimitRight())
            {
                debugLine("hit right");
                stopMotorRight();
                startupMachine();
                debugString("New State: ");
                debugLine(_currentState);
            }
            else if (!_isMotorRunningRight) //a scenario where the motor was stopped but it's not at the limit
            {
                debugLine("Startup right but motor not running");
                runMotorRight(false);
            }

            break;

        case STATE_CHECK_STARTUP_FORWARD:
            if (isLimitForward())
            {
                debugLine("hit forward");
                stopMotorForward();
                startupMachine();
                debugString("New State: ");
                debugLine(_currentState);
            }
            else if (!_isMotorRunningForward) //a scenario where the motor was stopped but it's not at the limit
            {
                debugLine("Startup forward but motor not running");
                runMotorForward(false);
            }

            break;

        case STATE_CHECK_HOMING_L:
            if (isLimitLeft())
            {
                debugLine("hit left");
                stopMotorLeft();
                performHoming();
                debugString("New State: ");
                debugLine(_currentState);
            } else if (!_isMotorRunningLeft) //a scenario where the motor was stopped but it's not at the limit
            {
                debugLine("Homing Left but motor not running");
                runMotorLeft(false);
            }

            break;
            
        case STATE_CHECK_HOMING_B:
            if (isLimitBackward())
            {
                debugLine("hit back");
                stopMotorBackward();
                performHoming();
                debugString("New State: ");
                debugLine(_currentState);
            }
            else if (!_isMotorRunningBackward) //a scenario where the motor was stopped but it's not at the limit
            {
                debugLine("Homing Backward but motor not running");
                runMotorBackward(false);
            }
            
            break;
        case STATE_CHECK_CENTERING:
            {
                unsigned long diffTime = millis() - _timestampRunCenter;
                
                bool isRightStopped = false;
                if (diffTime > _runToCenterDurationWidth)
                {
                    debugLine("width centered");
                    stopMotorRight();
                    isRightStopped = true;
                }
                if (diffTime > _runToCenterDurationDepth)
                {
                    debugLine("depth centered");
                    stopMotorForward();
                    if (isRightStopped)
                    {
                        
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
                delay(500); //brief rest for the claw
                dropClawProcedure();
            }
            else if (!_isMotorRunningDown) //a scenario where the motor was stopped but it's not at the limit
            {
                debugLine("Drop tension check but motor not running");
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
                debugLine("Drop recoil check but motor not running");
                runMotorUp(false);
            }

            break;
        case STATE_CHECK_RUNCHUTE_LEFT:
            if (isLimitLeft())
            {
                stopMotorLeft();
                delay(500); //brief rest for the claw
                returnToWinChute();
            }
            else if (!_isMotorRunningLeft) //a scenario where the motor was stopped but it's not at the limit
            {
                debugLine("Run chute left check but motor not running");
                runMotorLeft(false);
            }

            break;
        case STATE_CHECK_RUNCHUTE_BACK:
            if (isLimitBackward())
            {
                stopMotorBackward();
                delay(500); //brief rest for the claw
                returnToWinChute();
            }
            else if (!_isMotorRunningBackward) //a scenario where the motor was stopped but it's not at the limit
            {
                debugLine("Run chute backward but motor not running");
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
    debugLine("startup called");
    if (!isLimitUp())
    {
        debugLine("check recoil");
        changeState(STATE_CHECK_STARTUP_RECOIL);
        runMotorUp(false);
        return;
    }

    if (!isLimitRight())
    {
        debugLine("check right");
        changeState(STATE_CHECK_STARTUP_RIGHT);
        runMotorRight(false);
        return;
    }

    if (!isLimitForward())
    {
        debugLine("check forward");
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
        debugLine("check homing left");
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
        debugLine("check homing back");
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
        debugLine("run to center");
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
        returnToWinChute();
        return;
    }
    
    else //start drop if all else fails
    {
        changeState(STATE_CHECK_DROP_TENSION);
        sendEvent(EVENT_DROPPING_CLAW);
        runMotorDown(false);
        return;
    }
}

/**
 * 
 * run to left & front of machine, open claw, then returnCenterFromChute()
 * 
 */
void returnToWinChute()
{
    //startup
    if (_currentState == STATE_CHECK_DROP_RECOIL)
    {
        //recoil claw with force when returning home
        _recoilLimitOverride = true;
        runMotorUp(true);
        changeState(STATE_CHECK_RUNCHUTE_LEFT);
        runMotorLeft(false);
        return;
    }

    //ran left
    if (_currentState == STATE_CHECK_RUNCHUTE_LEFT)
    {
        changeState(STATE_CHECK_RUNCHUTE_BACK);
        runMotorBackward(false);
        return;
    }

    //ran back, over the win chute now
    if (_currentState == STATE_CHECK_RUNCHUTE_BACK)
    {
        sendEvent(EVENT_RETURNED_HOME);
        openClaw();

        delay(200);

        if (_doWiggle)
            wiggleClaw(); //blocking wiggle

        delay(250);
        returnCenterFromChute();
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
    debugLine("running centering");
    changeState(STATE_CHECK_CENTERING);
    runMotorRight(false);
    runMotorForward(false);
}




/**
 * 
 * EVENT HANDLING
 * 
 */
void handleSerialCommands()
{
    static byte sidx = 0; //serial cursor

    //if we have data, read it
    if (Serial.available())
    {
        char thisChar = Serial.read();
        if (thisChar == '\r' || thisChar == '\n')
        {
            _sincomingCommand[sidx] = '\0'; //terminate string
            char command[10]= {0}; //holds the command

            sscanf(_sincomingCommand, "%s", command);
            if (strcmp(command,".") == 0) { //ready to receive command
                if (_hasQueuedCommand) //bool for speed
                {
                    Serial.println(_queuedCommand);
                    Serial.flush();
                    _hasQueuedCommand = false;
                } else {
                    Serial.println("a"); //print ack
                    Serial.flush();
                }
            }
            sidx = 0;
        } else {
            //save our byte
            _sincomingCommand[sidx] = thisChar;
            sidx++;
            //prevent overlfow and reset to our last byte
            if (sidx >= _numChars) {
                sidx = _numChars - 1;
            }
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

void checkFlipperRuntime()
{
    if (_timestampConveyorFlipperStart > 0 && millis() - _timestampConveyorFlipperStart >= _conveyorFlipperRunTime)
    {
        digitalWrite(_PINConveyorFlipper, LOW);
        _timestampConveyorFlipperStart = 0;
    }
}

void checkBeltRuntime()
{
    if (_timestampConveyorBeltStart > 0 && millis() - _timestampConveyorBeltStart >= _conveyorBeltRunTime)
    {
        digitalWrite(_PINConveyorBelt, RELAYPINOFF);
        _timestampConveyorBeltStart = 0;
        _conveyorBeltRunTime = 0;
    }
}

void checkConveyorSensor()
{
    int sensorVal = digitalRead(_PINConveyorSensor);
    if (sensorVal == HIGH)
    {
        if (millis() - _timestampConveyorSensorTripped > _conveyorSensorTripDelay)
            sendEvent(EVENT_CONVEYOR_TRIPPED);
        
        _timestampConveyorSensorTripped = millis();
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
    if (_isDebugMode)
    {
        Serial.print(event);
        debugLine(outputData);
    }

    for (byte i=0; i < _clientCount; i++)
    {
        if (_clients[i] && _clients[i].connected())
            sendFormattedResponse(_clients[i], event, "0", outputData);
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
    static char outputData[400];
    static char sequence[_numChars]= {0}; //holds the command
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

        sprintf(outputData, "%i %i %i %i %i %i %s", _currentState, _lastState, _halfTimespanRunWidth, _halfTimespanRunDepth, _wiggleTime, _failsafeMotorLimit, _queuedCommand);
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
        sendFormattedResponse(client, EVENT_INFO, argument, outputData);

    } else if (strcmp(command,"pm") == 0) { //pin mode

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int pin = atoi(argument);
        int mode = atoi(argument2);
        pinMode(pin, mode);

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

    } else if (strcmp(command,"belt") == 0) { //move belt for # milliseconds

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        int val = atoi(argument);
        moveConveyorBelt(val);

    } else if (strcmp(command,"claw") == 0) { //open or close claw

        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        int val = atoi(argument);
        if (val == 1)
            closeClaw();
        else 
            openClaw();

    } else if (strcmp(command,"flip") == 0) { //move flipper out and back

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        startFlipper();

    } else if (strcmp(command,"light") == 0) { //lights on or off

        sendFormattedResponse(client, EVENT_INFO, sequence, "");

        if (strcmp(argument,"on") == 0)
        {
            digitalWrite(_PINLightsWhite, RELAYPINON);
        } else {
            digitalWrite(_PINLightsWhite, RELAYPINOFF);
        }
        
    } else if (strcmp(command,"clap") == 0) { //clap claw
        
        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        clapClaw();

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
        sprintf(_queuedCommand, "s %s %s %s %s %s %s", argument, argument2, argument3, argument4, argument5, argument6);
        _hasQueuedCommand = true;

    } else if (strcmp(command,"uno") == 0) { //send generic commands to uno

        sendFormattedResponse(client, EVENT_INFO, sequence, "");
        sprintf(_queuedCommand, "%s %s %s %s %s %s", argument, argument2, argument3, argument4, argument5, argument6);
        _hasQueuedCommand = true;

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
    if (runTime > 0)
    {
        digitalWrite(_PINConveyorBelt, RELAYPINON);
        _timestampConveyorBeltStart = millis();
        _conveyorBeltRunTime = runTime;
    } else if (runTime < 0) { //TODO - Build failsafe limit so motor can't accidentally run forever
        digitalWrite(_PINConveyorBelt, RELAYPINON);
    } else {
        digitalWrite(_PINConveyorBelt, RELAYPINOFF);
    }
}

void startFlipper()
{
    digitalWrite(_PINConveyorFlipper, HIGH);
    _timestampConveyorFlipperStart = millis();
    _conveyorFlipperRunTime = 3000;
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
        case CLAW_DROP:
            _clawRemoteMoveDurationDrop = duration;
            _clawRemoteMoveStartTimeDrop = millis();
            dropClawProcedure();
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
    debugLine("Method: Run Left Stop Right");
    stopMotorRight();
    if (!isLimitLeft() || override)
    {
        debugLine("Method: Run Left");
        _isMotorRunningLeft = true;
        _timestampMotorMoveLeft = millis();
        digitalWrite(_PINMoveLeft, RELAYPINON);
    }
    debugLine("Method: Run Left Done");
}

void runMotorRight(bool override)
{
    debugLine("Method: Run Right Stop Left");
    stopMotorLeft();
    if (!isLimitRight() || override)
    {
        debugLine("Method: Run Right");
        _isMotorRunningRight = true;
        _timestampMotorMoveRight = millis();
        digitalWrite(_PINMoveRight, RELAYPINON);
    }
    debugLine("Method: Run Right Done");
}

void runMotorForward(bool override)
{
    debugLine("Method: Run Forward Stop Backward");
    stopMotorBackward();
    if (!isLimitForward() || override)
    {
        debugLine("Method: Run Forward");
        _isMotorRunningForward = true;
        _timestampMotorMoveForward = millis();
        digitalWrite(_PINMoveForward, RELAYPINON);
    }
    debugLine("Method: Run Forward Done");
}

void runMotorBackward(bool override)
{
    debugLine("Method: Run Backward Stop Forward");
    stopMotorForward();
    if (!isLimitBackward() || override)
    {
        debugLine("Method: Run Backward");
        _isMotorRunningBackward = true;
        _timestampMotorMoveBackward = millis();
        digitalWrite(_PINMoveBackward, RELAYPINON);
    }
    debugLine("Method: Run Backward Done");
}

void runMotorDown(bool override)
{
    debugLine("Method: Run Down Stop Up");
    stopMotorUp();
    if (!isLimitDown() || override)
    {
        debugLine("Method: Run Down");
        _isMotorRunningDown = true;
        _timestampMotorMoveDown = millis();
        digitalWrite(_PINMoveDown, RELAYPINON);
    }
    debugLine("Method: Run Down Done");
}

void runMotorUp(bool override)
{
    debugLine("Method: Run Up Stop Down");
    stopMotorDown();
    if (!isLimitUp() || override)
    {
        debugLine("Method: Run Up");
        _isMotorRunningUp = true;
        _timestampMotorMoveUp = millis();
        digitalWrite(_PINMoveUp, RELAYPINON);
    }
    debugLine("Method: Run Up Done");
}

void stopMotorLeft()
{
    debugLine("Method: Stop Left");
    _isMotorRunningLeft = false;
    digitalWrite(_PINMoveLeft, RELAYPINOFF);
}

void stopMotorRight()
{
    debugLine("Method: Stop Right");
    _isMotorRunningRight = false;
    digitalWrite(_PINMoveRight, RELAYPINOFF);
}

void stopMotorForward()
{
    debugLine("Method: Stop Forward");
    _isMotorRunningForward = false;
    digitalWrite(_PINMoveForward, RELAYPINOFF);
}

void stopMotorBackward()
{
    debugLine("Method: Stop Backward");
    _isMotorRunningBackward = false;
    digitalWrite(_PINMoveBackward, RELAYPINOFF);
}

void stopMotorDown()
{
    debugLine("Method: Stop Down");
    _isMotorRunningDown = false;
    digitalWrite(_PINMoveDown, RELAYPINOFF);
}

void stopMotorUp()
{
    debugLine("Method: Stop Up");
    _isMotorRunningUp = false;
    digitalWrite(_PINMoveUp, RELAYPINOFF);
}

void debugLine(char* message)
{
    if (_isDebugMode)
        Serial.println(message);
}
void debugString(char* message)
{
    if (_isDebugMode)
        Serial.print(message);
}

void closeClaw()
{
    _timestampClawClosed = millis();
    _isClawClosed = true;
    digitalWrite(_PINClawPower, RELAYPINON);
}

void openClaw()
{
    _timestampClawClosed = 0;
    _isClawClosed = false;
    digitalWrite(_PINClawPower, RELAYPINOFF);
}
void changeState(int newState)
{
    if (_isDebugMode)
    {
        Serial.print("Change State - Old: ");
        Serial.print(_currentState);
        Serial.print(" New: ");
        Serial.println(newState);
    }
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

void clapClaw()
{
    closeClaw();
    delay(_wiggleTime);
    openClaw();
    delay(_wiggleTime);
    closeClaw();
    delay(_wiggleTime);
    openClaw();
    delay(_wiggleTime);
    closeClaw();
    delay(_wiggleTime);
    openClaw();
}