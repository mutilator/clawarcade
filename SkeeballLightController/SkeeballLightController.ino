/*

    Requirements?

    Light up individual hole
        - Set colors (rgb)
        - Pattern?
    Flash/strobe holes
        - Number of strobes
        - Color of strobes

    Patterns?
        - Circle hole
        - Light each in series
        - 


    Command Structure:
    1 byte = 0xFE
    1 byte = incoming bytes?
    1 byte = 0x01
    1 byte+ = args

    Slot definitions:
    0x06 = 10000 right
    0x05 = 10000 left
    0x04 = 5000
    0x03 = 4000
    0x02 = 3000
    0x01 = 2000
    0x00 = 1000
    
    
    

    Command - Show Slot: headerStart command headerEnd slotNumber red green blue
    e.g. light slot 1 with blue
    0xFE 0x01 0x01 0x00 0x00 0x00 0xFF

    Command - Strobe Slot: headerStart command headerEnd slotNumber red green blue red2 green2 blue2 strobeCount strobeDelay
    e.g. strube 5k slot 6 times, alternate between blue and red
    0xFE 0x01 0x01 0x04 0x00 0x00 0xFF 0xFF 0x00 0x00 0x15 0x60


*/


#include <avr/wdt.h>
#include <Wire.h>
#include "FastLED.h"


#if defined(FASTLED_VERSION) && (FASTLED_VERSION < 3001000)
#warning "Requires FastLED 3.1 or later; check github for latest code."
#endif
#define ARRAY_SIZE(A) (sizeof(A) / sizeof((A)[0]))
#define DATA_PIN    5
#define LED_TYPE    WS2812B
#define COLOR_ORDER GRB
#define NUM_LEDS 113
CRGB leds[NUM_LEDS];

#define BRIGHTNESS         32
#define FRAMES_PER_SECOND  120

const byte _slotLedCount = 16; // How many LED make up a single slot

const byte _slotStarts[] = { 96, 80, 64, 48, 32, 0, 16 }; // LED that starts this slot
unsigned long _slotStrobeTimestamp[] = { 0, 0, 0, 0, 0, 0, 0 }; // Timestamp of last strobe change
int _slotStrobeCurrentCount[] = { 0, 0, 0, 0, 0, 0, 0 }; // How many times have we strobbed
int _slotStrobeCount[] = { 0, 0, 0, 0, 0, 0, 0 }; // How many times should we strobe
int _slotStrobeDelay[] = { 0, 0, 0, 0, 0, 0, 0 }; // Delay per strobe
byte _slotStrobeRGB1[][3] = { { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 } }; // RGB1
byte _slotStrobeRGB2[][3] = { { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 } }; // RGB2

const byte _commandStart = 0xFE;
const byte _commandEnd = 0x01;

const byte _commandScoreSlot = 0x01; // Light a single slot
const byte _commandSpiralSlot = 0x02; // Spiral lights around a slot endlessly
const byte _commandFullPattern = 0x03; // Pattern that applies to all slots
const byte _commandStrobeSlot = 0x04; // Strobe a single slot

byte _commandBuffer[20];

void setup() {

    Wire.begin(0x10);
    Wire.onReceive(handleComms);
    

    // tell FastLED about the LED strip configuration
    FastLED.addLeds<LED_TYPE,DATA_PIN,COLOR_ORDER>(leds, NUM_LEDS).setCorrection(TypicalLEDStrip);
    //FastLED.addLeds<LED_TYPE,DATA_PIN,CLK_PIN,COLOR_ORDER>(leds, NUM_LEDS).setCorrection(TypicalLEDStrip);

    // set master brightness control
    FastLED.setBrightness(BRIGHTNESS);
    showSlot(0, random(255), random(255), random(255));
    showSlot(1, random(255), random(255), random(255));
    showSlot(2, random(255), random(255), random(255));
    showSlot(3, random(255), random(255), random(255));
    showSlot(4, random(255), random(255), random(255));
    showSlot(5, random(255), random(255), random(255));
    showSlot(6, random(255), random(255), random(255));
    showStrip();
}

void loop() {
    wdt_enable(WDTO_8S);

    runStrobes();
    executeCommandBuffer();

    // send the 'leds' array out to the actual LED strip
    //showStrip();

    // insert a delay to keep the framerate modest
    //FastLED.delay(8);
}

void executeCommandBuffer()
{
    if (_commandBuffer[0] != 0xFF)
        return;
    
    switch (_commandBuffer[1])
    {
        case _commandScoreSlot:
            showSlot(_commandBuffer[2], _commandBuffer[3], _commandBuffer[4], _commandBuffer[5]);
            break;
        case _commandStrobeSlot:
            strobeSlot(_commandBuffer[2], _commandBuffer[3], _commandBuffer[4], _commandBuffer[5], _commandBuffer[6], _commandBuffer[7], _commandBuffer[8], _commandBuffer[9], _commandBuffer[10]);
            break;
    }
    _commandBuffer[0] = 0;
}

void handleComms(int bytesRecv)
{
    int command = 0;
    while (Wire.available())
    {
        int byteRead = Wire.read();
        if (byteRead == _commandStart) //Is this the start of a command?
        {
            int commandByte = Wire.read(); // Save command sent
            byteRead = Wire.read(); //read byte after
            if (byteRead != _commandEnd) //Is this a proper end of command?
            {
                //burn the entire buffer because something invalid happened.
                while(Wire.available())
                    Wire.read();

                break;
            }
            command = commandByte;
        }
        int idx = 2;
        memset(_commandBuffer, 0, sizeof(_commandBuffer));
        _commandBuffer[0] = 0xFF; //we have a command ready
        _commandBuffer[1] = command;
        while(idx < sizeof(_commandBuffer)) //read up to 20 bytes
        {
            if (!Wire.available()) { return; }
            _commandBuffer[idx++] = Wire.read();
        }
    }
}



void runStrobes()
{
    for(int slot = 0; slot < 7; slot++)
    {
        if (_slotStrobeTimestamp[slot] > 0 && millis() - _slotStrobeTimestamp[slot] > _slotStrobeDelay[slot])
        {
            _slotStrobeCurrentCount[slot]++;
            if (_slotStrobeCurrentCount[slot] == _slotStrobeCount[slot])
            {
                _slotStrobeTimestamp[slot] = 0;
                continue;
            }

            _slotStrobeTimestamp[slot] = millis();

            if (_slotStrobeCurrentCount[slot] % 2)
                showSlot(slot, _slotStrobeRGB2[slot][0], _slotStrobeRGB2[slot][1], _slotStrobeRGB2[slot][2]);
            else
                showSlot(slot, _slotStrobeRGB1[slot][0], _slotStrobeRGB1[slot][1], _slotStrobeRGB1[slot][2]);

        }
    }
}




void strobeSlot(byte slot, byte r, byte g, byte b, byte r2, byte g2, byte b2, byte strobeCount, byte strobeDelay)
{
    _slotStrobeTimestamp[slot] = millis();
    _slotStrobeDelay[slot] = strobeDelay;
    _slotStrobeCount[slot] = strobeCount;
    _slotStrobeCurrentCount[slot] = 0;

    _slotStrobeRGB1[slot][0] = r;
    _slotStrobeRGB1[slot][1] = g;
    _slotStrobeRGB1[slot][2] = b;

    _slotStrobeRGB2[slot][0] = r2;
    _slotStrobeRGB2[slot][1] = g2;
    _slotStrobeRGB2[slot][2] = b2;

    showSlot(slot, r, g, b);   
}





void showSlot(byte slot, byte r, byte g, byte b)
{
    int slotStart = _slotStarts[slot];
    
    for(int slot = slotStart; slot < slotStart + _slotLedCount; slot++)
        setPixel(slot, r, g, b);
    showStrip();
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
