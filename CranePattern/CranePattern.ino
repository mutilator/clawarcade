#include "FastLED.h"

FASTLED_USING_NAMESPACE

#if defined(FASTLED_VERSION) && (FASTLED_VERSION < 3001000)
#warning "Requires FastLED 3.1 or later; check github for latest code."
#endif
#define ARRAY_SIZE(A) (sizeof(A) / sizeof((A)[0]))
#define DATA_PIN    5
#define LED_TYPE    WS2811
#define COLOR_ORDER GRB
#define PSU_PIN     11
#define NUM_LEDS 100
CRGB leds[NUM_LEDS];


#define BRIGHTNESS         128
#define FRAMES_PER_SECOND  120

const byte _numChars = 32;
char _incomingCommand[_numChars]; // an array to store the received data
char _acknowledgement = '\n'; //acknowledgement
unsigned long checkTime = 0;


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
  
void loop()
{
  
  
  // Call the current pattern function once, updating the 'leds' array
  gPatterns[gCurrentPatternNumber]();

  // send the 'leds' array out to the actual LED strip
  FastLED.show();

  // insert a delay to keep the framerate modest
  FastLED.delay(1000/FRAMES_PER_SECOND);

  // do some periodic updates
  EVERY_N_MILLISECONDS( 20 ) { gHue++; } // slowly cycle the "base color" through the rainbow

  EVERY_N_SECONDS( 30 ) { nextPattern(); } // change patterns periodically
  
    // do some periodic updates
  EVERY_N_MILLISECONDS( 100 ) { handleSerialCommands(); } // read from serial port
  
}

void handleSerialCommands()
{
  Serial.println(".");
  Serial.flush();
  checkTime = millis();
  //wait for response
  while (!Serial.available())
  {
    //only wait 2ms for response, not long enough?
    if (millis() - checkTime > 2)
      break;
  }
  checkTime = millis();
  //read data til there is no more
  static byte idx = 0;
  while (true)
  {
    if (Serial.available())
    {
      char thisChar = Serial.read();
      if (thisChar == '\n')
      {
        _incomingCommand[idx] = '\0'; //terminate string
        handleCommand();

        idx = 0;
        if (Serial.available())
          continue; //odd we have more data, restart loop, maybe the other controller got backed up
        else
          break; //ok no more data, break out and continue light show
      } else {
        if (thisChar != '\r') //ignore CR
        {
          //save our byte
          _incomingCommand[idx] = thisChar;
          idx++;
          //prevent overlfow and overwrite our last byte
          if (idx >= _numChars) {
              idx = _numChars - 1;
          }
        }
      }
    } else if (millis() - checkTime > 10) {
      idx = 0; //reset index to zero because the new data coming in should be a new command
      break;
    }
  }
}

void handleCommand()
{
  char outputData[400];
  char command[_numChars]= {0}; //holds the command
  char argument1[_numChars]= {0}; //holds the axis
  char argument2[_numChars]= {0}; //holds the setting
  char argument3[_numChars]= {0}; //holds the setting
  char argument4[_numChars]= {0}; //holds the setting
  char argument5[_numChars]= {0}; //holds the setting
  char argument6[_numChars]= {0}; //holds the setting

  //inefficient but safer, everything is a string then converted later
  sscanf(_incomingCommand, "%s %s %s %s %s %s %s", command, argument1, argument2, argument3, argument4, argument5, argument6);

  if (strcmp(command,"a") == 0) { //ack, do nothing

  }
  else if (strcmp(command,"s") == 0) { //strobe
    
    byte red = (byte)atoi(argument1);
    byte green = (byte)atoi(argument2);
    byte blue = (byte)atoi(argument3);

    int StrobeCount = atoi(argument4);
    int FlashDelay = atoi(argument5);
    int EndPause = atoi(argument6);
    
    strobe(red, green, blue, StrobeCount, FlashDelay, EndPause);
    
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

    int StrobeCount = atoi(argument3);
    int FlashDelay = atoi(argument4);
    int EndPause = atoi(argument5);
    
    dualstrobe(red, green, blue, red2, green2, blue2, StrobeCount, FlashDelay, EndPause);
    
   } else if (strcmp(command,"p") == 0)
   {
     int power = atoi(argument1);
     analogWrite(PSU_PIN, power);
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
  showStrip();
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
void strobe(byte red, byte green, byte blue, int StrobeCount, int FlashDelay, int EndPause){
  for(int j = 0; j < StrobeCount; j++) {
    setAll(red,green,blue);
    showStrip();
    delay(FlashDelay);
    setAll(0,0,0);
    showStrip();
    delay(FlashDelay);
  }
 
 delay(EndPause);
}

void dualstrobe(byte red, byte green, byte blue, byte red2, byte green2, byte blue2, int StrobeCount, int FlashDelay, int EndPause){
  for(int j = 0; j < StrobeCount; j++) {
    setAll(red,green,blue);
    showStrip();
    delay(FlashDelay);
    setAll(0,0,0);
    showStrip();
    delay(FlashDelay);

    setAll(red2,green2,blue2);
    showStrip();
    delay(FlashDelay);
    setAll(0,0,0);
    showStrip();
    delay(FlashDelay);
  }
 
 delay(EndPause);
}
