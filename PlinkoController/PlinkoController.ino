#include "FastLED.h"

FASTLED_USING_NAMESPACE

HardwareSerial &usbController = Serial;
HardwareSerial &mainController = Serial1; //PIN 18 & 19

/***********************************
 * 
 *    LED STUFF
 * 
 ********************************/
#define ARRAY_SIZE(A) (sizeof(A) / sizeof((A)[0]))
#define LED_TYPE    WS2812B
#define COLOR_ORDER GRB

#define NUM_STRIPS    2

#define DATA_PIN_STAGE_1    22
#define DATA_PIN_STAGE_2    23

#define NUM_LEDS_STAGE_1 59
#define NUM_LEDS_STAGE_2 46

CRGB leds_stage_1[NUM_LEDS_STAGE_1];
CRGB leds_stage_2[NUM_LEDS_STAGE_2];

CRGB leds_stage_1_rainbow[NUM_LEDS_STAGE_1];
CRGB leds_stage_2_rainbow[NUM_LEDS_STAGE_2];

CLEDController *controllers[NUM_STRIPS];

#define BRIGHTNESS         32
#define FRAMES_PER_SECOND  120


bool _isDebugMode = false;

const int _numChars = 60;

const byte _slotStage11[] = { 0, 1, 2, 3, 58, 57, 56, 55, 7, 6, 5, 4 };
const byte _slotStage12[] = { 4, 5, 6, 7, 54, 53, 52, 51, 11, 10, 9, 8 };
const byte _slotStage13[] = { 8, 9, 10, 11, 50, 49, 48, 47, 15, 14, 13, 12 };
const byte _slotStage14[] = { 12, 13, 14, 15, 46, 45, 44, 43, 19, 18, 17, 16 };
const byte _slotStage15[] = { 16, 17, 18, 19, 43, 42, 41, 40, 23, 22, 21, 20 };
const byte _slotStage16[] = { 20, 21, 22, 23, 39, 38, 37, 36, 27, 26, 25, 24 };
const byte _slotStage17[] = { 24, 25, 26, 27, 35, 34, 33, 32, 31, 30, 29, 28 };

const byte _slotStage11Size = 12;
const byte _slotStage12Size = 12;
const byte _slotStage13Size = 12;
const byte _slotStage14Size = 12;
const byte _slotStage15Size = 12;
const byte _slotStage16Size = 12;
const byte _slotStage17Size = 12;

const byte _slotStage25[] = { 4, 5, 6, 7, 41, 42, 43, 44, 45, 3, 2, 1, 0 };
const byte _slotStage24[] = { 8, 9, 10, 11, 37, 38, 39, 40, 7, 6, 5, 4 };
const byte _slotStage23[] = { 12, 13, 14, 15, 33, 34, 35, 36, 11, 10, 9, 8 };
const byte _slotStage22[] = { 16, 17, 18, 19, 28, 29, 30, 31, 32, 15, 14, 13, 12 };
const byte _slotStage21[] = { 20, 21, 22, 23, 24, 25, 26, 27, 28, 19, 18, 17, 16 };

const byte _slotStage21Size = 13;
const byte _slotStage22Size = 13;
const byte _slotStage23Size = 12;
const byte _slotStage24Size = 12;
const byte _slotStage25Size = 13;

//SCORING 
const int _PINStage1LatchReset = 24;
const int _PINStage2LatchReset = 25;

const int _PINStage1Sensor1 = 41;//40; //plinko top board
const int _PINStage1Sensor2 = 40;//38;
const int _PINStage1Sensor3 = 38;//36;
const int _PINStage1Sensor4 = 36;//35;
const int _PINStage1Sensor5 = 35;//37;
const int _PINStage1Sensor6 = 37;//39;
const int _PINStage1Sensor7 = 39;//41;

const int _PINStage2Sensor1 = 42; //plinko bottom board
const int _PINStage2Sensor2 = 43;
const int _PINStage2Sensor3 = 47;
const int _PINStage2Sensor4 = 45;
const int _PINStage2Sensor5 = 49;

const int EVENT_SCORE_SENSOR = 108; 

unsigned long _timestampStage1ScoreSensor1 = 0;
unsigned long _timestampStage1ScoreSensor2 = 0;
unsigned long _timestampStage1ScoreSensor3 = 0;
unsigned long _timestampStage1ScoreSensor4 = 0;
unsigned long _timestampStage1ScoreSensor5 = 0;
unsigned long _timestampStage1ScoreSensor6 = 0;
unsigned long _timestampStage1ScoreSensor7 = 0;

unsigned long _timestampStage2ScoreSensor1 = 0;
unsigned long _timestampStage2ScoreSensor2 = 0;
unsigned long _timestampStage2ScoreSensor3 = 0;
unsigned long _timestampStage2ScoreSensor4 = 0;
unsigned long _timestampStage2ScoreSensor5 = 0;

unsigned long _timestampStage1Sensor = 0; //Time we last flashed
unsigned long _timestampStage2Sensor = 0; //Time we last flashed

byte _stage1SlotFlashing = 0; // handles which slot is flashing in stage 1 - 0 = none
byte _stage2SlotFlashing = 0; // handles which slot is flashing in stage 2 - 0 = none

byte _stage1FlashesComplete = 0; // handles which slot is flashing in stage 1 - 0 = none
byte _stage2FlashesComplete = 0; // handles which slot is flashing in stage 2 - 0 = none

int _scoreSensorDelay = 1000; // how long before we look at a sensor again

bool _needsSecondaryInit = true; //used to initialize pins after setup()

byte _fadeBy = 10; //how quick to fade trails

int SENSOR_TRIPPED_STATE = HIGH;

char _sPlinkoIncomingCommand[_numChars]; // an array to store the received data
char _sUsbPlinkoIncomingCommand[_numChars];

byte _redColor = 255;
byte _blueColor = 0;
byte _greenColor = 0;
bool _showRainbow = false;
bool _staticRainbow = false;

// TRACER DISPLAY VARIABLES
bool _tracerEnabled = false;
bool _wasTracertEnabled = false;
int _tracerStage1Index = 0;
int _tracerStage2Index = 0;
byte _tracerStage1Dir = 0;
byte _tracerStage2Dir = 0;
unsigned long _timestampTacer = 0;

//U Tracer
bool _utracerEnabled = false;
bool _wasuTracertEnabled = false;
byte _utracerStage1Index = 0;
byte _utracerStage2Index = 0;
byte _utracerStage1Dir = 0;
byte _utracerStage2Dir = 0;
int _utracerStage1SlotIndex = 0;
int _utracerStage2SlotIndex = 0;
unsigned long _timestampuTacer = 0;


//Reverse U Tracer
bool _urtracerEnabled = false;
bool _wasurTracertEnabled = false;
byte _urtracerStage1Index = 0;
byte _urtracerStage2Index = 0;
byte _urtracerStage1Dir = 0;
byte _urtracerStage2Dir = 0;
int _urtracerStage1SlotIndex = 0;
int _urtracerStage2SlotIndex = 0;
unsigned long _timestampurTacer = 0;

// BLINK ALL DISPLAY VARIABLES
bool _blinkAllEnabled = false;
bool _wasBlinkAllEnabled = false;
unsigned long _timestampLastBlinkedAll = 0;
bool _blinkAllOn = false;

char _lastPlinkoMessage[40]; //plinko message
unsigned long _waitForAckTimestamp = 0; //time we sent last plinko message
byte _waitForAckCount = 0;

uint8_t gHue = 0; // rotating "base color" used by many of the patterns
const char RTS = '{'; //request to send data
const char CS = '}'; //complete send data
const char CTS = '!'; //clear to send data

void setup() {
    delay(2000);
  
    mainController.begin(250000);
    usbController.begin(115200);

    // tell FastLED about the LED strip configuration
    controllers[0] = &FastLED.addLeds<LED_TYPE,DATA_PIN_STAGE_1,COLOR_ORDER>(leds_stage_1, NUM_LEDS_STAGE_1).setCorrection(TypicalLEDStrip);
    controllers[1] = &FastLED.addLeds<LED_TYPE,DATA_PIN_STAGE_2,COLOR_ORDER>(leds_stage_2, NUM_LEDS_STAGE_2).setCorrection(TypicalLEDStrip);
    //FastLED.addLeds<LED_TYPE,DATA_PIN,CLK_PIN,COLOR_ORDER>(leds, NUM_LEDS).setCorrection(TypicalLEDStrip);

    // set master brightness control
    FastLED.setBrightness(BRIGHTNESS);
}

void loop()
{
    if (_needsSecondaryInit)
    {
        initScoreSensors();
        _needsSecondaryInit = false;
    }


    handlePlinkoSerialCommands();
    handleUsbSerialCommands();
    handleSlotFlashing();
    checkScoreSensors();

    runTracer();
    runUTracer();
    runURTracer();
    runFlashAll();

    EVERY_N_MILLISECONDS( 20 ) { gHue++; } // slowly cycle the "base color" through the rainbow


    FastLED.delay(8);

}

void initScoreSensors()
{
    pinMode(_PINStage1Sensor1, INPUT);
    pinMode(_PINStage1Sensor2, INPUT);
    pinMode(_PINStage1Sensor3, INPUT);
    pinMode(_PINStage1Sensor4, INPUT);
    pinMode(_PINStage1Sensor5, INPUT);
    pinMode(_PINStage1Sensor6, INPUT);
    pinMode(_PINStage1Sensor7, INPUT);

    pinMode(_PINStage2Sensor1, INPUT);
    pinMode(_PINStage2Sensor2, INPUT);
    pinMode(_PINStage2Sensor3, INPUT);
    pinMode(_PINStage2Sensor4, INPUT);
    pinMode(_PINStage2Sensor5, INPUT);

    pinMode(_PINStage1LatchReset, OUTPUT);
    pinMode(_PINStage2LatchReset, OUTPUT);
    digitalWrite(_PINStage1LatchReset, HIGH);
    digitalWrite(_PINStage2LatchReset, HIGH);
}


/**
 *
 * Claw functionality
 *
 */
void checkScoreSensors()
{
    static unsigned long curTime = 0;
    curTime = millis();

    if (digitalRead(_PINStage1Sensor1) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage1Sensor1);
        debugLine(".");
       
        if (curTime - _timestampStage1ScoreSensor1 > _scoreSensorDelay)
        {
            _timestampStage1ScoreSensor1 = curTime;
            triggerFlashing(1);
            sendScoreSensorClear(_PINStage1LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "1");
        }
    }

    if (digitalRead(_PINStage1Sensor2) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage1Sensor2);
        debugLine(".");
       
        if (curTime - _timestampStage1ScoreSensor2 > _scoreSensorDelay)
        {
            _timestampStage1ScoreSensor2 = curTime;
            triggerFlashing(2);
            sendScoreSensorClear(_PINStage1LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "2");
        }
    }

    if (digitalRead(_PINStage1Sensor3) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage1Sensor3);
        debugLine(".");
       
        if (curTime - _timestampStage1ScoreSensor3 > _scoreSensorDelay)
        {
            _timestampStage1ScoreSensor3 = curTime;
            triggerFlashing(3);
            sendScoreSensorClear(_PINStage1LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "3");
        }
    }

    if (digitalRead(_PINStage1Sensor4) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage1Sensor4);
        debugLine(".");
       
        if (curTime - _timestampStage1ScoreSensor4 > _scoreSensorDelay)
        {
            _timestampStage1ScoreSensor4 = curTime;
            triggerFlashing(4);
            sendScoreSensorClear(_PINStage1LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "4");
        }
    }

    if (digitalRead(_PINStage1Sensor5) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage1Sensor5);
        debugLine(".");
       
        if (curTime - _timestampStage1ScoreSensor5 > _scoreSensorDelay)
        {
            _timestampStage1ScoreSensor5 = curTime;
            triggerFlashing(5);
            sendScoreSensorClear(_PINStage1LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "5");
        }
    }

    if (digitalRead(_PINStage1Sensor6) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage1Sensor6);
        debugLine(".");
       
        if (curTime - _timestampStage1ScoreSensor6 > _scoreSensorDelay)
        {
            _timestampStage1ScoreSensor6 = curTime;
            triggerFlashing(6);
            sendScoreSensorClear(_PINStage1LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "6");
        }
    }

    if (digitalRead(_PINStage1Sensor7) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage1Sensor7);
        debugLine(".");
       
        if (curTime - _timestampStage1ScoreSensor7 > _scoreSensorDelay)
        {
            _timestampStage1ScoreSensor7 = curTime;
            triggerFlashing(7);
            sendScoreSensorClear(_PINStage1LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "7");
        }
    }


    // STAGE 2



    if (digitalRead(_PINStage2Sensor1) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage2Sensor1);
        debugLine(".");
       
        if (curTime - _timestampStage2ScoreSensor1 > _scoreSensorDelay)
        {
            _timestampStage2ScoreSensor1 = curTime;
            triggerFlashing(8);
            sendScoreSensorClear(_PINStage2LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "8");
        }
    }

    if (digitalRead(_PINStage2Sensor2) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage2Sensor2);
        debugLine(".");
       
        if (curTime - _timestampStage2ScoreSensor2 > _scoreSensorDelay)
        {
            _timestampStage2ScoreSensor2 = curTime;
            triggerFlashing(9);
            sendScoreSensorClear(_PINStage2LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "9");
        }
    }

    if (digitalRead(_PINStage2Sensor3) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage2Sensor3);
        debugLine(".");
       
        if (curTime - _timestampStage2ScoreSensor3 > _scoreSensorDelay)
        {
            _timestampStage2ScoreSensor3 = curTime;
            triggerFlashing(10);
            sendScoreSensorClear(_PINStage2LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "10");
        }
    }

    if (digitalRead(_PINStage2Sensor4) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage2Sensor4);
        debugLine(".");
       
        if (curTime - _timestampStage2ScoreSensor4 > _scoreSensorDelay)
        {
            _timestampStage2ScoreSensor4 = curTime;
            triggerFlashing(11);
            sendScoreSensorClear(_PINStage2LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "11");
        }
    }

    if (digitalRead(_PINStage2Sensor5) == SENSOR_TRIPPED_STATE)
    {
        debugString("sensor hit ");
        debugString(_PINStage2Sensor5);
        debugLine(".");
       
        if (curTime - _timestampStage2ScoreSensor5 > _scoreSensorDelay)
        {
            _timestampStage2ScoreSensor5 = curTime;
            triggerFlashing(12);
            sendScoreSensorClear(_PINStage2LatchReset);
            sendSerialEvent(EVENT_SCORE_SENSOR, "12");
        }
    }
}

//set clear to the flipflop holding the tripped slots
void sendScoreSensorClear(int pin)
{
    digitalWrite(pin, LOW);
    delay(50);
    digitalWrite(pin, HIGH);
}

/*

  SERIAL HANDLING

*/

void notifyMainControllerMessage()
{
    _waitForAckTimestamp = millis();
    mainController.print(RTS);
    mainController.flush();
}

void sendMainControllerMessage(char message[])
{
    notifyMainControllerMessage();
    strncpy(_lastPlinkoMessage, message, strlen(message)+1);
    
}

void sendMainControllerData(char message[])
{
    mainController.print(message);
    mainController.print(CS);
}


//command from plinko is ".command args!"
void handlePlinkoSerialCommands()
{
    //if we sent a message but didn't receive an ACK then send again
    if (_waitForAckTimestamp && millis() - _waitForAckTimestamp > 300)
    {
        _waitForAckCount++;
        notifyMainControllerMessage();
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
    while(mainController.available() > 0) //burns through the buffer waiting for a start byte
    {
        char thisChar = mainController.read();
        if (thisChar == RTS)
        {
            mainController.print(CTS);
            mainController.flush();

            

            //reset the index
            sidx = 0;

            while (millis() - startTime < 300) //wait up to 300ms for next byte
            {
                if (mainController.available() <= 0) //if nothing new.. continue
                    continue;
                

                startTime = millis(); //update received timestamp, allows slow data to come in (manually typing)
                thisChar = mainController.read();
                if (thisChar == RTS)
                    continue;
                if (thisChar == CS) //if we receive a proper ending, process the data
                {
                    while (mainController.available() > 0) //burns the buffer
                        mainController.read();

                    _sPlinkoIncomingCommand[sidx] = '\0'; //terminate string

                    //ADD COMMAND HANDLER
                    handleSerialCommand(_sPlinkoIncomingCommand);
                    break; //jump out
                } else {
                    //save our byte1
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
            _waitForAckCount = 0;
            _waitForAckTimestamp = 0;

            sendMainControllerData(_lastPlinkoMessage);
        }
    }
}

void handleUsbSerialCommands()
{
    static byte sidx = 0; //serial cursor
    static unsigned long startTime = 0; //memory placeholder

    startTime = millis();

    //if we have data, read it
    while (usbController.available()) //burns through the buffer waiting for a start byte
    {
        char thisChar = usbController.read(); 
        if (thisChar == RTS) //is this a start byte?
        {
            while (millis() - startTime < 1000) //wait up to 1000ms for next byte
            {
                if (!usbController.available())
                    continue;
                
                startTime = millis(); //update received timestamp, allows slow data to come in (manually typing)
                thisChar = usbController.read();

                if (thisChar == CS) //if we receive a proper ending, process the data
                {
                    _sUsbPlinkoIncomingCommand[sidx] = '\0'; //terminate string
                    
                    //ADD COMMAND HANDLER
                    handleSerialCommand(_sUsbPlinkoIncomingCommand);
                    break;
                } else {

                    //save our byte
                    _sUsbPlinkoIncomingCommand[sidx] = thisChar;
                    sidx++;

                    //prevent overlfow and reset to our last byte
                    if (sidx >= _numChars) {
                        sidx = _numChars - 1;
                    }
                }
            }
            //we either processed data from a successful command or the command timed out
            
            //reset the index
            sidx = 0;
            break;
        }
    }
}

void handleSerialCommand(char *incomingData)
{
    static char command[_numChars]= {0}; //holds the command
    static char argument1[_numChars]= {0}; //holds the axis
    static char argument2[_numChars]= {0}; //holds the axis
    static char argument3[_numChars]= {0}; //holds the axis

    sscanf(incomingData, "%s %s %s %s", command, argument1,argument2, argument3);

    if (strcmp(command,"dbg") == 0) { //debug
       _isDebugMode = !_isDebugMode;
    } else if (strcmp(command,"b") == 0) { //blink a specific slot
        int arg = atoi(argument1);
        showSlot(arg);
    } else if (strcmp(command,"pr") == 0) { //pin read
        int pin = atoi(argument1);
        usbController.println(digitalRead(pin));
    }
     else if (strcmp(command,"pm") == 0) { //pin mode
        int pin = atoi(argument1);
        int arg = atoi(argument2);
        pinMode(pin, arg);
    } else if (strcmp(command,"pw") == 0) { //pin write
        int pin = atoi(argument1);
        int arg = atoi(argument2);
        digitalWrite(pin, arg);
    } else if (strcmp(command,"r") == 0) { //restart machine
        int stage = atoi(argument1);
        if (stage == 1)
        {
            digitalWrite(_PINStage1LatchReset, LOW);
            delay(100);
            digitalWrite(_PINStage1LatchReset, HIGH);
        } else {
            digitalWrite(_PINStage2LatchReset, LOW);
            delay(100);
            digitalWrite(_PINStage2LatchReset, HIGH);
        }
    } else if (strcmp(command,"sc") == 0) { //set colors
        int r = atoi(argument1);
        int g = atoi(argument2);
        int b = atoi(argument3);
        _redColor = r;
        _greenColor = g;
        _blueColor = b;
    } else if (strcmp(command,"fb") == 0) { //set colors
        _fadeBy = atoi(argument1);
    } else if (strcmp(command,"rb") == 0) { //set colors
        _showRainbow = atoi(argument1) == 1;
        _staticRainbow = atoi(argument1) == 2;
        if (_staticRainbow)
        {
            fill_rainbow(leds_stage_1_rainbow, NUM_LEDS_STAGE_1, gHue, 7);
            fill_rainbow(leds_stage_2_rainbow, NUM_LEDS_STAGE_2, gHue, 7);
        }
    } else if (strcmp(command,"pat") == 0) { //display pattern
        int pattern = atoi(argument1);
        switch (pattern)
        {
            case 1: //tracer
                stopAllPatterns();
                startTracer();
                break;
            case 2: //blink everything
                stopAllPatterns();
                startFlashAll();
                break;
            case 3:
                stopAllPatterns();
                startUTracer();
                break;
            case 4:
                stopAllPatterns();
                startURTracer();
                break;
            default: //disable patterns
            //case 0: 
                stopAllPatterns();
                setAll(0, 0, 0, 0);
                setAll(1, 0, 0, 0);
                break;
        }
    }

}

void sendSerialEvent(int eventId, char outputData[])
{
    sprintf(_lastPlinkoMessage, "%i %s", eventId, outputData);
    sendMainControllerMessage(_lastPlinkoMessage);
    debugString("TX: ");
    debugString(eventId);
    debugString(" ");
    debugString(outputData);
    debugLine("-ETX");
}





/*
 * ***************** *
 *                   *
 *   LED HANDLING    *
 *                   *
 * ***************** *
*/

void restartPatterns()
{
    if (_wasTracertEnabled)
    {
        startTracer();
    } else if (_wasBlinkAllEnabled)
    {
        startFlashAll();
    } else if (_wasuTracertEnabled)
    {
        startUTracer();
    } else if (_wasurTracertEnabled)
    {
        startURTracer();
    }
}
void stopAllPatterns()
{
    stopTracer();
    stopFlashAll();
    stopUTracer();
    stopURTracer();
    setAll(0, 0, 0, 0);
    setAll(1, 0, 0, 0);
}

void startURTracer()
{
    _wasurTracertEnabled = false;
    _urtracerEnabled = true;
    //UR TRACERT INIT
    _urtracerStage1Index = 7;
    _urtracerStage1SlotIndex = _slotStage17Size;
    _urtracerStage2Index = 5;
    _urtracerStage2SlotIndex = _slotStage25Size;
    _urtracerStage1Dir = 1;
    _urtracerStage2Dir = 1;

    //U TRACER INIT
    _utracerStage1Index = 1;
    _utracerStage2Index = 1;
    _utracerStage1SlotIndex = 0;
    _utracerStage2SlotIndex = 0;
    _utracerStage1Dir = -1;
    _utracerStage2Dir = -1;

    runURTracer();
}


void stopURTracer()
{
    _urtracerEnabled = false;
}

void runURTracer()
{
    if (!_urtracerEnabled)
        return;

    
    fadeToBlackBy(leds_stage_1, NUM_LEDS_STAGE_1, _fadeBy);
    fadeToBlackBy(leds_stage_2, NUM_LEDS_STAGE_2, _fadeBy);

    if (millis() - _timestampurTacer < 50)
        return;

    _timestampurTacer = millis();

    realRunUTracer();

    int displayIndex1 = 0; // -1 means we reached the end of a slot moving forward, -2 means we reached the start of a slot going in reverse
    int displayIndex2 = 0;
    switch (_urtracerStage1Index)
    {
        case 1:
            displayIndex1 = getNextURTracerLed1(_slotStage11, _slotStage11Size, _slotStage11Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextURTracerLed1(_slotStage12, _slotStage12Size, _slotStage11Size);
            else if (displayIndex1 == -2)
            {
                //we reached the beginning going in reverse, swap directions
                _urtracerStage1Index = 1;
                _urtracerStage1Dir = 0;
                _urtracerStage1SlotIndex = -1;
                displayIndex1 = getNextURTracerLed1(_slotStage11, _slotStage11Size, _slotStage11Size);
            }
        break;
        case 2:
            displayIndex1 = getNextURTracerLed1(_slotStage12, _slotStage12Size, _slotStage11Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextURTracerLed1(_slotStage13, _slotStage13Size, _slotStage12Size);
            else if (displayIndex1 == -2)
                displayIndex1 = getNextURTracerLed1(_slotStage11, _slotStage11Size, _slotStage11Size);
        break;
        case 3:
            displayIndex1 = getNextURTracerLed1(_slotStage13, _slotStage13Size, _slotStage12Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextURTracerLed1(_slotStage14, _slotStage14Size, _slotStage13Size);
            else if (displayIndex1 == -2)
                displayIndex1 = getNextURTracerLed1(_slotStage12, _slotStage12Size, _slotStage11Size);
        break;
        case 4:
            displayIndex1 = getNextURTracerLed1(_slotStage14, _slotStage14Size, _slotStage13Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextURTracerLed1(_slotStage15, _slotStage15Size, _slotStage14Size);
            else if (displayIndex1 == -2)
                displayIndex1 = getNextURTracerLed1(_slotStage13, _slotStage13Size, _slotStage12Size);
        break;
        case 5:
            displayIndex1 = getNextURTracerLed1(_slotStage15, _slotStage15Size, _slotStage14Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextURTracerLed1(_slotStage16, _slotStage16Size, _slotStage15Size);
            else if (displayIndex1 == -2)
                displayIndex1 = getNextURTracerLed1(_slotStage14, _slotStage14Size, _slotStage13Size);
        break;
        case 6:
            displayIndex1 = getNextURTracerLed1(_slotStage16, _slotStage16Size, _slotStage15Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextURTracerLed1(_slotStage17, _slotStage17Size, _slotStage16Size);
            else if (displayIndex1 == -2)
                displayIndex1 = getNextURTracerLed1(_slotStage15, _slotStage15Size, _slotStage14Size);
            
        break;
        case 7:
            displayIndex1 = getNextURTracerLed1(_slotStage17, _slotStage17Size, _slotStage16Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
            {
                _urtracerStage1Dir = 1; //swap directions
                _urtracerStage1SlotIndex = _slotStage17Size;
                _urtracerStage1Index = 7;
                
                displayIndex1 = getNextURTracerLed1(_slotStage17, _slotStage17Size, _slotStage16Size);
                
            } else if (displayIndex1 == -2)
            {
                displayIndex1 = getNextURTracerLed1(_slotStage16, _slotStage16Size, _slotStage15Size);
            }
        break;
        default:
        break;
    }

    switch (_urtracerStage2Index)
    {
        case 1:
            displayIndex2 = getNextURTracerLed2(_slotStage21, _slotStage21Size, _slotStage21Size);
            if (displayIndex2 == -1) //if we've reached the end of the line
                displayIndex2 = getNextURTracerLed2(_slotStage22, _slotStage22Size, _slotStage21Size);
            else if (displayIndex2 == -2)
            {
                //we reached the beginning going in reverse, swap directions
                _urtracerStage2Index = 1;
                _urtracerStage2Dir = 0;
                _urtracerStage2SlotIndex = -1;
                displayIndex2 = getNextURTracerLed2(_slotStage21, _slotStage21Size, _slotStage21Size);
            }
        break;
        case 2:
            displayIndex2 = getNextURTracerLed2(_slotStage22, _slotStage22Size, _slotStage21Size);
            if (displayIndex2 == -1) //if we've reached the end of the line
                displayIndex2 = getNextURTracerLed2(_slotStage23, _slotStage23Size, _slotStage22Size);
            else if (displayIndex2 == -2)
                displayIndex2 = getNextURTracerLed2(_slotStage21, _slotStage21Size, _slotStage21Size);
        break;
        case 3:
            displayIndex2 = getNextURTracerLed2(_slotStage23, _slotStage23Size, _slotStage22Size);
            if (displayIndex2 == -1) //if we've reached the end of the line
                displayIndex2 = getNextURTracerLed2(_slotStage24, _slotStage24Size, _slotStage23Size);
            else if (displayIndex2 == -2)
                displayIndex2 = getNextURTracerLed2(_slotStage22, _slotStage22Size, _slotStage21Size);
        break;
        case 4:
            displayIndex2 = getNextURTracerLed2(_slotStage24, _slotStage24Size, _slotStage23Size);
            if (displayIndex2 == -1) //if we've reached the end of the line
                displayIndex2 = getNextURTracerLed2(_slotStage25, _slotStage25Size, _slotStage24Size);
            else if (displayIndex2 == -2)
                displayIndex2 = getNextURTracerLed2(_slotStage23, _slotStage23Size, _slotStage22Size);
        break;
        case 5:
            displayIndex2 = getNextURTracerLed2(_slotStage25, _slotStage25Size, _slotStage24Size);
            if (displayIndex2 == -1) //if we've reached the end of the line
            {
                _urtracerStage2Dir = 1; //swap directions
                _urtracerStage2SlotIndex = _slotStage25Size;
                _urtracerStage2Index = 5;
                
                displayIndex2 = getNextURTracerLed2(_slotStage25, _slotStage25Size, _slotStage24Size);
            } else if (displayIndex2 == -2)
                displayIndex2 = getNextURTracerLed2(_slotStage24, _slotStage24Size, _slotStage23Size);
        break;
        default:
        break;
    }
    debugString("u1 - D: ");
    debugString(_urtracerStage1Dir);
    debugString(" S: ");
    debugString(_urtracerStage1Index);
    debugString(" I: ");
    debugString(_urtracerStage1SlotIndex);
    debugString(" L: ");
    debugString(displayIndex1);
    debugLine("");

    debugString("u2 - D: ");
    debugString(_urtracerStage2Dir);
    debugString(" S: ");
    debugString(_urtracerStage2Index);
    debugString(" I: ");
    debugString(_urtracerStage2SlotIndex);
    debugString(" L: ");
    debugString(displayIndex2);
    debugLine("");

    //display pixel
    if (_showRainbow)
    {
        leds_stage_1[displayIndex1] = CHSV( 255 - gHue, 200, 255);
        leds_stage_2[displayIndex2] = CHSV( 255 - gHue, 200, 255);
    } else if (_staticRainbow)
    {
        
        leds_stage_1[displayIndex1].r = leds_stage_1_rainbow[displayIndex1].r;
        leds_stage_1[displayIndex1].g = leds_stage_1_rainbow[displayIndex1].g;
        leds_stage_1[displayIndex1].b = leds_stage_1_rainbow[displayIndex1].b;

        leds_stage_2[displayIndex2].r = leds_stage_2_rainbow[displayIndex2].r;
        leds_stage_2[displayIndex2].g = leds_stage_2_rainbow[displayIndex2].g;
        leds_stage_2[displayIndex2].b = leds_stage_2_rainbow[displayIndex2].b;
        
    } else {
        setPixel(leds_stage_1[displayIndex1], _blueColor, _greenColor, _redColor);
        setPixel(leds_stage_2[displayIndex2], _blueColor, _greenColor, _redColor);
    }
  
}

int getNextURTracerLed1(byte slotStageValues[], int slotStageCurrentSize, int slotStagePrevSize)
{
    if (_urtracerStage1Dir == 1) //moving in reverse now
    {
        _urtracerStage1SlotIndex--;
        if (_urtracerStage1SlotIndex < 0) //if we've reached the beginning, move to the next slot
        {
            _urtracerStage1SlotIndex = slotStagePrevSize;
            _urtracerStage1Index--;
            return -2;
        } else {
            return slotStageValues[_urtracerStage1SlotIndex]; //return slot value
        }
    } else { //moving forward
        _urtracerStage1SlotIndex++;
        if (_urtracerStage1SlotIndex >= slotStageCurrentSize) //if we've reached the end, move to the next slot
        {
            _urtracerStage1SlotIndex = -1;
            _urtracerStage1Index++;
            return -1;
        }

        return slotStageValues[_urtracerStage1SlotIndex];
    }
}

int getNextURTracerLed2(byte slotStageValues[], int slotStageCurrentSize, int slotStagePrevSize)
{
    if (_urtracerStage2Dir == 1) //moving in reverse now
    {
        _urtracerStage2SlotIndex--;
        if (_urtracerStage2SlotIndex < 0) //if we've reached the beginning, move to the next slot
        {
            _urtracerStage2SlotIndex = slotStagePrevSize;
            _urtracerStage2Index--;
            return -2;
        } else {
            return slotStageValues[_urtracerStage2SlotIndex]; //return slot value
        }
    } else { //moving forward
        _urtracerStage2SlotIndex++;
        if (_urtracerStage2SlotIndex >= slotStageCurrentSize) //if we've reached the end, move to the next slot
        {
            _urtracerStage2SlotIndex = -1;
            _urtracerStage2Index++;
            return -1;
        }

        return slotStageValues[_urtracerStage2SlotIndex];
    }
}


/*

   U TRACER CODE

*/

void startUTracer()
{
    _wasuTracertEnabled = false;
    _utracerEnabled = true;
    _utracerStage1Index = 1;
    _utracerStage2Index = 1;
    _utracerStage1SlotIndex = 0;
    _utracerStage2SlotIndex = 0;
    _utracerStage1Dir = -1;
    _utracerStage2Dir = -1;
    runUTracer();
}

void runUTracer()
{
    if (!_utracerEnabled)
        return;

    fadeToBlackBy(leds_stage_1, NUM_LEDS_STAGE_1, _fadeBy);
    fadeToBlackBy(leds_stage_2, NUM_LEDS_STAGE_2, _fadeBy);

    if (millis() - _timestampuTacer < 50)
        return;

    _timestampuTacer = millis();
    realRunUTracer();
  
}
void realRunUTracer()
{
    int displayIndex1 = 0; // -1 means we reached the end of a slot moving forward, -2 means we reached the start of a slot going in reverse
    int displayIndex2 = 0;
    switch (_utracerStage1Index)
    {
        case 1:
            displayIndex1 = getNextUTracerLed1(_slotStage11, _slotStage11Size, _slotStage11Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextUTracerLed1(_slotStage12, _slotStage12Size, _slotStage11Size);
            else if (displayIndex1 == -2)
            {
                //we reached the beginning going in reverse, swap directions
                _utracerStage1Index = 1;
                _utracerStage1Dir = 0;
                _utracerStage1SlotIndex = -1;
                displayIndex1 = getNextUTracerLed1(_slotStage11, _slotStage11Size, _slotStage11Size);
            }
        break;
        case 2:
            displayIndex1 = getNextUTracerLed1(_slotStage12, _slotStage12Size, _slotStage11Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextUTracerLed1(_slotStage13, _slotStage13Size, _slotStage12Size);
            else if (displayIndex1 == -2)
                displayIndex1 = getNextUTracerLed1(_slotStage11, _slotStage11Size, _slotStage11Size);
        break;
        case 3:
            displayIndex1 = getNextUTracerLed1(_slotStage13, _slotStage13Size, _slotStage12Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextUTracerLed1(_slotStage14, _slotStage14Size, _slotStage13Size);
            else if (displayIndex1 == -2)
                displayIndex1 = getNextUTracerLed1(_slotStage12, _slotStage12Size, _slotStage11Size);
        break;
        case 4:
            displayIndex1 = getNextUTracerLed1(_slotStage14, _slotStage14Size, _slotStage13Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextUTracerLed1(_slotStage15, _slotStage15Size, _slotStage14Size);
            else if (displayIndex1 == -2)
                displayIndex1 = getNextUTracerLed1(_slotStage13, _slotStage13Size, _slotStage12Size);
        break;
        case 5:
            displayIndex1 = getNextUTracerLed1(_slotStage15, _slotStage15Size, _slotStage14Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextUTracerLed1(_slotStage16, _slotStage16Size, _slotStage15Size);
            else if (displayIndex1 == -2)
                displayIndex1 = getNextUTracerLed1(_slotStage14, _slotStage14Size, _slotStage13Size);
        break;
        case 6:
            displayIndex1 = getNextUTracerLed1(_slotStage16, _slotStage16Size, _slotStage15Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
                displayIndex1 = getNextUTracerLed1(_slotStage17, _slotStage17Size, _slotStage16Size);
            else if (displayIndex1 == -2)
                displayIndex1 = getNextUTracerLed1(_slotStage15, _slotStage15Size, _slotStage14Size);
            
        break;
        case 7:
            displayIndex1 = getNextUTracerLed1(_slotStage17, _slotStage17Size, _slotStage16Size);
            if (displayIndex1 == -1) //if we've reached the end of the line
            {
                _utracerStage1Dir = 1; //swap directions
                _utracerStage1SlotIndex = _slotStage17Size;
                _utracerStage1Index = 7;
                
                displayIndex1 = getNextUTracerLed1(_slotStage17, _slotStage17Size, _slotStage16Size);
                
            } else if (displayIndex1 == -2)
            {
                displayIndex1 = getNextUTracerLed1(_slotStage16, _slotStage16Size, _slotStage15Size);
            }
        break;
        default:
        break;
    }

    switch (_utracerStage2Index)
    {
        case 1:
            displayIndex2 = getNextUTracerLed2(_slotStage21, _slotStage21Size, _slotStage21Size);
            if (displayIndex2 == -1) //if we've reached the end of the line
                displayIndex2 = getNextUTracerLed2(_slotStage22, _slotStage22Size, _slotStage21Size);
            else if (displayIndex2 == -2)
            {
                //we reached the beginning going in reverse, swap directions
                _utracerStage2Index = 1;
                _utracerStage2Dir = 0;
                _utracerStage2SlotIndex = -1;
                displayIndex2 = getNextUTracerLed2(_slotStage21, _slotStage21Size, _slotStage21Size);
            }
        break;
        case 2:
            displayIndex2 = getNextUTracerLed2(_slotStage22, _slotStage22Size, _slotStage21Size);
            if (displayIndex2 == -1) //if we've reached the end of the line
                displayIndex2 = getNextUTracerLed2(_slotStage23, _slotStage23Size, _slotStage22Size);
            else if (displayIndex2 == -2)
                displayIndex2 = getNextUTracerLed2(_slotStage21, _slotStage21Size, _slotStage21Size);
        break;
        case 3:
            displayIndex2 = getNextUTracerLed2(_slotStage23, _slotStage23Size, _slotStage22Size);
            if (displayIndex2 == -1) //if we've reached the end of the line
                displayIndex2 = getNextUTracerLed2(_slotStage24, _slotStage24Size, _slotStage23Size);
            else if (displayIndex2 == -2)
                displayIndex2 = getNextUTracerLed2(_slotStage22, _slotStage22Size, _slotStage21Size);
        break;
        case 4:
            displayIndex2 = getNextUTracerLed2(_slotStage24, _slotStage24Size, _slotStage23Size);
            if (displayIndex2 == -1) //if we've reached the end of the line
                displayIndex2 = getNextUTracerLed2(_slotStage25, _slotStage25Size, _slotStage24Size);
            else if (displayIndex2 == -2)
                displayIndex2 = getNextUTracerLed2(_slotStage23, _slotStage23Size, _slotStage22Size);
        break;
        case 5:
            displayIndex2 = getNextUTracerLed2(_slotStage25, _slotStage25Size, _slotStage24Size);
            if (displayIndex2 == -1) //if we've reached the end of the line
            {
                _utracerStage2Dir = 1; //swap directions
                _utracerStage2SlotIndex = _slotStage25Size;
                _utracerStage2Index = 5;
                
                displayIndex2 = getNextUTracerLed2(_slotStage25, _slotStage25Size, _slotStage24Size);
            } else if (displayIndex2 == -2)
                displayIndex2 = getNextUTracerLed2(_slotStage24, _slotStage24Size, _slotStage23Size);
        break;
        default:
        break;
    }
    debugString("1 - D: ");
    debugString(_utracerStage1Dir);
    debugString(" S: ");
    debugString(_utracerStage1Index);
    debugString(" I: ");
    debugString(_utracerStage1SlotIndex);
    debugString(" L: ");
    debugString(displayIndex1);
    debugLine("");

    debugString("2 - D: ");
    debugString(_utracerStage2Dir);
    debugString(" S: ");
    debugString(_utracerStage2Index);
    debugString(" I: ");
    debugString(_utracerStage2SlotIndex);
    debugString(" L: ");
    debugString(displayIndex2);
    debugLine("");

    //display pixel
    if (_showRainbow)
    {
        leds_stage_1[displayIndex1] = CHSV( gHue, 200, 255);
        leds_stage_2[displayIndex2] = CHSV( gHue, 200, 255);
    }  else if (_staticRainbow)
    {
        leds_stage_1[displayIndex1].r = leds_stage_1_rainbow[displayIndex1].r;
        leds_stage_1[displayIndex1].g = leds_stage_1_rainbow[displayIndex1].g;
        leds_stage_1[displayIndex1].b = leds_stage_1_rainbow[displayIndex1].b;

        leds_stage_2[displayIndex2].r = leds_stage_2_rainbow[displayIndex2].r;
        leds_stage_2[displayIndex2].g = leds_stage_2_rainbow[displayIndex2].g;
        leds_stage_2[displayIndex2].b = leds_stage_2_rainbow[displayIndex2].b;
    } else {
        setPixel(leds_stage_1[displayIndex1], _redColor, _greenColor, _blueColor);
        setPixel(leds_stage_2[displayIndex2], _redColor, _greenColor, _blueColor);
    }
}

int getNextUTracerLed1(byte slotStageValues[], int slotStageCurrentSize, int slotStagePrevSize)
{
    if (_utracerStage1Dir == 1) //moving in reverse now
    {
        _utracerStage1SlotIndex--;
        if (_utracerStage1SlotIndex < 0) //if we've reached the beginning, move to the next slot
        {
            _utracerStage1SlotIndex = slotStagePrevSize;
            _utracerStage1Index--;
            return -2;
        } else {
            return slotStageValues[_utracerStage1SlotIndex]; //return slot value
        }
    } else { //moving forward
        _utracerStage1SlotIndex++;
        if (_utracerStage1SlotIndex >= slotStageCurrentSize) //if we've reached the end, move to the next slot
        {
            _utracerStage1SlotIndex = -1;
            _utracerStage1Index++;
            return -1;
        }

        return slotStageValues[_utracerStage1SlotIndex];
    }
}

int getNextUTracerLed2(byte slotStageValues[], int slotStageCurrentSize, int slotStagePrevSize)
{
    if (_utracerStage2Dir == 1) //moving in reverse now
    {
        _utracerStage2SlotIndex--;
        if (_utracerStage2SlotIndex < 0) //if we've reached the beginning, move to the next slot
        {
            _utracerStage2SlotIndex = slotStagePrevSize;
            _utracerStage2Index--;
            return -2;
        } else {
            return slotStageValues[_utracerStage2SlotIndex]; //return slot value
        }
    } else { //moving forward
        _utracerStage2SlotIndex++;
        if (_utracerStage2SlotIndex >= slotStageCurrentSize) //if we've reached the end, move to the next slot
        {
            _utracerStage2SlotIndex = -1;
            _utracerStage2Index++;
            return -1;
        }

        return slotStageValues[_utracerStage2SlotIndex];
    }
}

void stopUTracer()
{
    _utracerEnabled = false;
}

void startTracer()
{
    _wasTracertEnabled = false;
    _tracerEnabled = true;
    _tracerStage1Index = 0;
    _tracerStage2Index = 0;
    _tracerStage1Dir = 0;
    _tracerStage2Dir = 0;
    runTracer();
}

void stopTracer()
{
    _tracerEnabled = false;
}

void runTracer()
{
    if (!_tracerEnabled)
        return;

    fadeToBlackBy(leds_stage_1, NUM_LEDS_STAGE_1, _fadeBy);
    fadeToBlackBy(leds_stage_2, NUM_LEDS_STAGE_2, _fadeBy);

    if (millis() - _timestampTacer < 50)
        return;

    _timestampTacer = millis();
    //display pixel
    if (_showRainbow)
    {
        leds_stage_1[_tracerStage1Index] = CHSV( gHue, 200, 255);
        leds_stage_2[_tracerStage2Index] = CHSV( gHue, 200, 255);
    } else if (_staticRainbow)
    {
        leds_stage_1[_tracerStage1Index].r = leds_stage_1_rainbow[_tracerStage1Index].r;
        leds_stage_1[_tracerStage1Index].g = leds_stage_1_rainbow[_tracerStage1Index].g;
        leds_stage_1[_tracerStage1Index].b = leds_stage_1_rainbow[_tracerStage1Index].b;

        leds_stage_2[_tracerStage2Index].r = leds_stage_2_rainbow[_tracerStage2Index].r;
        leds_stage_2[_tracerStage2Index].g = leds_stage_2_rainbow[_tracerStage2Index].g;
        leds_stage_2[_tracerStage2Index].b = leds_stage_2_rainbow[_tracerStage2Index].b;
    } else {
        setPixel(leds_stage_1[_tracerStage1Index], _redColor, _greenColor, _blueColor);
        setPixel(leds_stage_2[_tracerStage2Index], _redColor, _greenColor, _blueColor);
    }
    
    
    //verify we're not at our bounds STAGE 1
    if (_tracerStage1Index >= NUM_LEDS_STAGE_1-1)  
    {
        _tracerStage1Dir = 1;
    } else if (_tracerStage1Index <= 0)
    {
        _tracerStage1Dir = 0;
    }

    //verify we're not at our bounds
    if (_tracerStage2Index >= NUM_LEDS_STAGE_2-1)
    {
        _tracerStage2Dir = 1;
    } else if (_tracerStage2Index <= 0)
    {
        _tracerStage2Dir = 0;
    }

        //increase or decrease pixel we're working on
    if (_tracerStage1Dir == 1)
        _tracerStage1Index--;
    else
        _tracerStage1Index++;

    if (_tracerStage2Dir == 1)
        _tracerStage2Index--;
    else
        _tracerStage2Index++;

}

void startFlashAll()
{
    _wasBlinkAllEnabled = false;
    _blinkAllEnabled = true;
    _timestampLastBlinkedAll = 0;
    _blinkAllOn = true;
    runFlashAll();
}
void stopFlashAll()
{
    _blinkAllEnabled = false;
}

void runFlashAll()
{
    if (!_blinkAllEnabled)
        return;

    if (millis() - _timestampLastBlinkedAll > 100)
    {
        _timestampLastBlinkedAll = millis();
        _blinkAllOn = !_blinkAllOn;
        if (_blinkAllOn)
        {
            setAll(0, 0, 0, 0);
            setAll(1, 0, 0, 0);
        }
        else 
        {
            setAll(0, _redColor, _greenColor, _blueColor);
            setAll(1, _redColor, _greenColor, _blueColor);
        }

    }
}

void handleSlotFlashing()
{
    if (_stage1SlotFlashing > 0)
    {
        if (millis() - _timestampStage1Sensor > 100)
        {
            switch (_stage1SlotFlashing)
            { 
                //STAGE 1
                case 1:
                    handleStage1Flashing(_slotStage11, _slotStage11Size);
                    break;
                case 2:
                    handleStage1Flashing(_slotStage12, _slotStage12Size);
                    break;
                case 3:
                    handleStage1Flashing(_slotStage13, _slotStage13Size);
                    break;
                case 4:
                    handleStage1Flashing(_slotStage14, _slotStage14Size);
                    break;
                case 5:
                    handleStage1Flashing(_slotStage15, _slotStage15Size);
                    break;
                case 6:
                    handleStage1Flashing(_slotStage16, _slotStage16Size);
                    break;
                case 7:
                    handleStage1Flashing(_slotStage17, _slotStage17Size);
                    break;
            }
        }
    }

    if (_stage2SlotFlashing > 0)
    {
        if (millis() - _timestampStage2Sensor > 100)
        {
            switch (_stage2SlotFlashing)
            {
                case 1:
                    handleStage2Flashing(_slotStage21, _slotStage21Size);
                    break;
                case 2:
                    handleStage2Flashing(_slotStage22, _slotStage22Size);
                    break;
                case 3:
                    handleStage2Flashing(_slotStage23, _slotStage23Size);
                    break;
                case 4:
                    handleStage2Flashing(_slotStage24, _slotStage24Size);
                    break;
                case 5:
                    handleStage2Flashing(_slotStage25, _slotStage25Size);
                    break;
            }
        }
    }
}

void handleStage1Flashing(byte ledSlots[], byte slotSize)
{
    _timestampStage1Sensor = millis();
    _stage1FlashesComplete++;
    if (_stage1FlashesComplete > 7)
    {
        _stage1SlotFlashing = 0;
        _timestampStage1Sensor = 0;
        _stage1FlashesComplete = 0;
        restartPatterns();
    } else if (_stage1FlashesComplete % 2 == 0)
    {
        lightSlot(0, ledSlots, slotSize);                
    } else {
        setAll(0, 0, 0, 0);
    }
}

void handleStage2Flashing(byte ledSlots[], byte slotSize)
{
    _timestampStage2Sensor = millis();
    _stage2FlashesComplete++;
    if (_stage2FlashesComplete > 7)
    {
        _stage2SlotFlashing = 0;
        _timestampStage2Sensor = 0;
        _stage2FlashesComplete = 0;
        restartPatterns();
    } else if (_stage2FlashesComplete % 2 == 0)
    {
        lightSlot(1, ledSlots, slotSize);                
    } else {
        setAll(1, 0, 0, 0);
    }
}

void triggerFlashing(int slot)
{
    if (_tracerEnabled)
        _wasTracertEnabled = true;
    if (_blinkAllEnabled)
        _wasBlinkAllEnabled = true;

    stopAllPatterns();

    switch (slot)
    { 
        //STAGE 1
        case 1:
            _timestampStage1Sensor = millis();
            _stage1SlotFlashing = 1;
            lightSlot(0, _slotStage11, _slotStage11Size);
            break;
        case 2:
            _timestampStage1Sensor = millis();
            _stage1SlotFlashing = 2;
            lightSlot(0, _slotStage12, _slotStage12Size);
            break;
        case 3:
            _timestampStage1Sensor = millis();
            _stage1SlotFlashing = 3;
            lightSlot(0, _slotStage13, _slotStage13Size);
            break;
        case 4:
            _timestampStage1Sensor = millis();
            _stage1SlotFlashing = 4;
            lightSlot(0, _slotStage14, _slotStage14Size);
            break;
        case 5:
            _timestampStage1Sensor = millis();
            _stage1SlotFlashing = 5;
            lightSlot(0, _slotStage15, _slotStage15Size);
            break;
        case 6:
            _timestampStage1Sensor = millis();
            _stage1SlotFlashing = 6;
            lightSlot(0, _slotStage16, _slotStage16Size);
            break;
        case 7:
            _timestampStage1Sensor = millis();
            _stage1SlotFlashing = 7;
            lightSlot(0, _slotStage17, _slotStage17Size);
            break;

        //STAGE 2
        case 8:
            _timestampStage2Sensor = millis();
            _stage2SlotFlashing = 1;
            lightSlot(1, _slotStage21, _slotStage21Size);
            break;
        case 9:
            _timestampStage2Sensor = millis();
            _stage2SlotFlashing = 2;
            lightSlot(1, _slotStage22, _slotStage22Size);
            break;
        case 10:
            _timestampStage2Sensor = millis();
            _stage2SlotFlashing = 3;
            lightSlot(1, _slotStage23, _slotStage23Size);
            break;
        case 11:
            _timestampStage2Sensor = millis();
            _stage2SlotFlashing = 4;
            lightSlot(1, _slotStage24, _slotStage24Size);
            break;
        case 12:
            _timestampStage2Sensor = millis();
            _stage2SlotFlashing = 5;
            lightSlot(1, _slotStage25, _slotStage25Size);
            break;
    }

}

void showSlot(byte slot)
{
    switch (slot)
    { 
        //STAGE 1
        case 1:
            flashSlot(0, _slotStage11, _slotStage11Size);
            break;
        case 2:
            flashSlot(0, _slotStage12, _slotStage12Size);
            break;
        case 3:
            flashSlot(0, _slotStage13, _slotStage13Size);
            break;
        case 4:
            flashSlot(0, _slotStage14, _slotStage14Size);
            break;
        case 5:
            flashSlot(0, _slotStage15, _slotStage15Size);
            break;
        case 6:
            flashSlot(0, _slotStage16, _slotStage16Size);
            break;
        case 7:
            flashSlot(0, _slotStage17, _slotStage17Size);
            break;

        //STAGE 2
        case 8:
            flashSlot(1, _slotStage21, _slotStage21Size);
            break;
        case 9:
            flashSlot(1, _slotStage22, _slotStage22Size);
            break;
        case 10:
            flashSlot(1, _slotStage23, _slotStage23Size);
            break;
        case 11:
            flashSlot(1, _slotStage24, _slotStage24Size);
            break;
        case 12:
            flashSlot(1, _slotStage25, _slotStage25Size);
            break;
    }
}

void flashSlot(int controller, byte ledSlots[], byte slotSize)
{
    for(byte flashes = 0; flashes < 5; flashes++)
    {
        
        lightSlot(controller, ledSlots, slotSize);
        delay(100);
        setAll(controller, 0, 0, 0);
        delay(100);
    }
}


void lightSlot(int controller, byte ledSlots[], byte slotSize)
{
    CRGB *ledArray = leds_stage_1;
    switch (controller)
    {
        case 1:
            ledArray = leds_stage_2;
            break;
        default:
            ledArray = leds_stage_1;
            break;
    }

    for(byte i = 0; i < slotSize; i++)
    {
        setPixel(ledArray[ledSlots[i]], _redColor, _greenColor, _blueColor);
    }

    controllers[controller]->showLeds(BRIGHTNESS);
}


void setAll(int controller, byte red, byte green, byte blue)
{
    switch (controller)
    {
        case 1:
            for(int i = 0; i < NUM_LEDS_STAGE_2; i++ )
                setPixel(leds_stage_2[i], red, green, blue); 
            
            break;
        default:
            for(int i = 0; i < NUM_LEDS_STAGE_1; i++ )
                    setPixel(leds_stage_1[i], red, green, blue); 
            
            break;
    }

    controllers[controller]->showLeds(BRIGHTNESS);
}

void showStrip() {
  FastLED.show();
}

void setPixel(CRGB &pixel, byte red, byte green, byte blue) {
  pixel.r = red;
  pixel.g = green;
  pixel.b = blue;
}

void debugLine(char message[])
{
    if (_isDebugMode)
    {
        usbController.println(message);
    }
}

void debugString(char message[])
{
    if (_isDebugMode)
    {
        usbController.print(message);
    }
}

void debugString(int message)
{
    if (_isDebugMode)
    {
        usbController.print(message);
    }
}

void debugByte(byte message)
{
    if (_isDebugMode)
    {
        usbController.print(message);
    }
}
