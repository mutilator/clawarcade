#include "FastLED.h"

FASTLED_USING_NAMESPACE

#define ARRAY_SIZE(A) (sizeof(A) / sizeof((A)[0]))
#define DATA_PIN    5
#define LED_TYPE    WS2811
#define COLOR_ORDER GRB
#define PSU_PIN     11
#define NUM_LEDS 31
CRGB leds[NUM_LEDS];


#define BRIGHTNESS         128
#define FRAMES_PER_SECOND  120

const int _PINButton = 3;
const int _PINRelay = 53;

const byte _numChars = 64;
char _incomingCommand[_numChars]; // an array to store the received data

unsigned long checkTime = 0;
const char RTS = '{'; //request to send data
const char CS = '}'; //comeplete send data
const char CTS = '!'; //clear to send data

void setup() {
  delay(2000);
  Serial.begin(115200);
  pinMode(PSU_PIN, OUTPUT);
  analogWrite(PSU_PIN, 165);
  // tell FastLED about the LED strip configuration
  FastLED.addLeds<LED_TYPE,DATA_PIN,COLOR_ORDER>(leds, NUM_LEDS).setCorrection(TypicalLEDStrip);
  //FastLED.addLeds<LED_TYPE,DATA_PIN,CLK_PIN,COLOR_ORDER>(leds, NUM_LEDS).setCorrection(TypicalLEDStrip);

  // set master brightness control
  FastLED.setBrightness(BRIGHTNESS);
}


// List of patterns to cycle through.  Each is defined as a separate function below.
typedef void (*SimplePatternList[])();
SimplePatternList gPatterns = { rainbow, rainbowWithGlitter, confetti, sinelon, juggle, bpm };

uint8_t gCurrentPatternNumber = 0; // Index number of which pattern is current
uint8_t gHue = 0; // rotating "base color" used by many of the patterns
bool _lightsEnabled = true;

char _lastLedMessage[40]; //led message
unsigned long _waitForLedAckTimestamp = 0; //time we sent last led message
byte _waitForLedAckCount = 0; //retry counter

void loop()
{

  // Call the current pattern function once, updating the 'leds' array
  if (_lightsEnabled)
    gPatterns[gCurrentPatternNumber]();



  // do some periodic updates
  EVERY_N_MILLISECONDS( 20 ) { gHue++; } // slowly cycle the "base color" through the rainbow

  EVERY_N_SECONDS( 30 ) { nextPattern(); } // change patterns periodically

  handleSerialCommands();

  // insert a delay to keep the framerate modest
  FastLED.delay(8);
}

void notifySerialMessage()
{
    _waitForLedAckTimestamp = millis();
    Serial.print(RTS);
    Serial.flush();
    
}

void sendSerialMessage(char message[])
{
    notifySerialMessage();
    strncpy(_lastLedMessage, message, strlen(message)+1);
}

void sendSerialData(char message[])
{
    Serial.print(message);
    Serial.print(CS);
    Serial.flush();
}

void handleSerialCommands()
{
    //if we sent a message but didn't receive an ACK then send again
    if (_waitForLedAckTimestamp && millis() - _waitForLedAckTimestamp > 300)
    {
        _waitForLedAckCount++;
        notifySerialMessage();
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
    while (Serial.available() > 0) //burns through the buffer waiting for a start byte
    {
      char thisChar = Serial.read();

      if (thisChar == RTS) //if the other end wants to send data, tell them it's OK
      {
        Serial.print(CTS);
        Serial.flush();

        
        //no we wait for data to come in
        sidx = 0;
        
        while (millis() - startTime < 300) //wait up to 300ms for next byte
        {
          if (!Serial.available()) //if nothing new.. continue
              continue;

          startTime = millis(); //update received timestamp, allows slow data to come in (manually typing)
          thisChar = Serial.read();
          if (thisChar == RTS)
                    continue;
          if (thisChar == CS) //if we receive a proper ending, process the data
          {
            while (Serial.available() > 0) //burns the buffer
              Serial.read();

            _incomingCommand[sidx] = '\0'; //terminate string
            handleCommand();
            break;
          } else {
            //save our byte
            _incomingCommand[sidx] = thisChar;
            sidx++;
            //prevent overlfow and reset to our last byte
            if (sidx >= _numChars) {
                sidx = _numChars - 1;
            }
          } 
        } //end data while loop
      } //end RTS check
      else if (thisChar == CTS)
      {
          _waitForLedAckTimestamp = 0;
          _waitForLedAckCount = 0;

          sendSerialData(_lastLedMessage);
      }
    } //end initial while loop
}

void handleCommand()
{
  char outputData[100];
  char command[_numChars]= {0}; //holds the command
  char argument1[_numChars]= {0}; //holds the axis
  char argument2[_numChars]= {0}; //holds the setting
  char argument3[_numChars]= {0}; //holds the setting
  char argument4[_numChars]= {0}; //holds the setting
  char argument5[_numChars]= {0}; //holds the setting
  char argument6[_numChars]= {0}; //holds the setting

  //inefficient but safer, everything is a string then converted later
  sscanf(_incomingCommand, "%s %s %s %s %s %s %s", command, argument1, argument2, argument3, argument4, argument5, argument6);

  if (strcmp(command,"s") == 0) //strobe
  {

    byte red = (byte)atoi(argument1);
    byte green = (byte)atoi(argument2);
    byte blue = (byte)atoi(argument3);

    int strobeCount = atoi(argument4);
    int flashDelay = atoi(argument5);
    int endPause = atoi(argument6);

    strobe(red, green, blue, strobeCount, flashDelay, endPause);

   }
   else if (strcmp(command,"ds") == 0) { //police strobe
    char sred[_numChars]= {0}; //holds the setting
    char sgreen[_numChars]= {0}; //holds the setting
    char sblue[_numChars]= {0}; //holds the setting
    sscanf(argument1, "%[^:]:%[^:]:%[^:]", sred, sgreen, sblue);
    byte red = (byte)atoi(sred);
    byte green = (byte)atoi(sgreen);
    byte blue = (byte)atoi(sblue);

    sscanf(argument2, "%[^:]:%[^:]:%[^:]", sred, sgreen, sblue);
    byte red2 = (byte)atoi(sred);
    byte green2 = (byte)atoi(sgreen);
    byte blue2 = (byte)atoi(sblue);

    int strobeCount = atoi(argument3);
    int flashDelay = atoi(argument4);
    int endPause = atoi(argument5);

    dualstrobe(red, green, blue, red2, green2, blue2, strobeCount, flashDelay, endPause);

   } else if (strcmp(command,"p") == 0)
   {
     int power = atoi(argument1);
     analogWrite(PSU_PIN, power);
   } else if (strcmp(command,"lm") == 0) //light mode
   {
      _lightsEnabled = atoi(argument1) == 1;
      if (!_lightsEnabled)
      {
        setAll(0,0,0);
        showStrip();
      }
   }
}


void showStrip() {
  FastLED.show();
}
void setPixel(int Pixel, byte red, byte green, byte blue) {
  leds[Pixel].r = red;
  leds[Pixel].g = green;
  leds[Pixel].b = blue;
}

void setAll(byte red, byte green, byte blue) {
  for(int i = 0; i < NUM_LEDS; i++ ) {
    setPixel(i, red, green, blue); 
  }
}


void nextPattern()
{
  // add one to the current pattern number, and wrap around at the end
  gCurrentPatternNumber = (gCurrentPatternNumber + 1) % ARRAY_SIZE( gPatterns);
}

void rainbow() 
{
  // FastLED's built-in rainbow generator
  fill_rainbow( leds, NUM_LEDS, gHue, 7);
}

void rainbowWithGlitter() 
{
  // built-in FastLED rainbow, plus some random sparkly glitter
  rainbow();
  addGlitter(80);
}

void addGlitter( fract8 chanceOfGlitter) 
{
  if( random8() < chanceOfGlitter) {
    leds[ random16(NUM_LEDS) ] += CRGB::White;
  }
}

void confetti() 
{
  // random colored speckles that blink in and fade smoothly
  fadeToBlackBy( leds, NUM_LEDS, 10);
  int pos = random16(NUM_LEDS);
  leds[pos] += CHSV( gHue + random8(64), 200, 255);
}

void sinelon()
{
  // a colored dot sweeping back and forth, with fading trails
  fadeToBlackBy( leds, NUM_LEDS, 20);
  int pos = beatsin16( 13, 0, NUM_LEDS-1 );
  leds[pos] += CHSV( gHue, 255, 192);
}

void bpm()
{
  // colored stripes pulsing at a defined Beats-Per-Minute (BPM)
  uint8_t BeatsPerMinute = 62;
  CRGBPalette16 palette = PartyColors_p;
  uint8_t beat = beatsin8( BeatsPerMinute, 64, 255);
  for( int i = 0; i < NUM_LEDS; i++) { //9948
    leds[i] = ColorFromPalette(palette, gHue+(i*2), beat-gHue+(i*10));
  }
}

void juggle() {
  // eight colored dots, weaving in and out of sync with each other
  fadeToBlackBy( leds, NUM_LEDS, 20);
  byte dothue = 0;
  for( int i = 0; i < 8; i++) {
    leds[beatsin16( i+7, 0, NUM_LEDS-1 )] |= CHSV(dothue, 200, 255);
    dothue += 32;
  }
}

void strobe(byte red, byte green, byte blue, int strobeCount, int flashDelay, int endPause){
  for(int j = 0; j < strobeCount; j++) {
    setAll(red,green,blue);
    FastLED.delay(flashDelay);
    setAll(0,0,0);
    FastLED.delay(flashDelay);
  }
 
 delay(endPause);
}

void dualstrobe(byte red, byte green, byte blue, byte red2, byte green2, byte blue2, int strobeCount, int flashDelay, int endPause){
  for(int j = 0; j < strobeCount; j++) {
    setAll(red,green,blue);
    FastLED.delay(flashDelay);
    setAll(0,0,0);
    FastLED.delay(flashDelay);

    setAll(red2,green2,blue2);
    FastLED.delay(flashDelay);
    setAll(0,0,0);
    FastLED.delay(flashDelay);
  }
 
 delay(endPause);
}
