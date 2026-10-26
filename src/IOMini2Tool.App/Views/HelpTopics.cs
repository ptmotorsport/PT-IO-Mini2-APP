using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace IOMini2Tool.Views;

/// <summary>
/// Attached property marking a section of the UI with a help topic id, plus the static
/// topic content shown by HelpWindow. Set HelpTopics.Topic="..." on a GroupBox or panel;
/// pressing F1 while hovering any part of it opens HelpWindow scrolled to that topic.
/// </summary>
public static class HelpTopics
{
    public static readonly DependencyProperty TopicProperty =
        DependencyProperty.RegisterAttached("Topic", typeof(string), typeof(HelpTopics), new PropertyMetadata(null));

    public static void SetTopic(DependencyObject element, string value) => element.SetValue(TopicProperty, value);

    public static string? GetTopic(DependencyObject element) => (string?)element.GetValue(TopicProperty);

    public sealed record Topic(string Id, string Title, string Body);

    public const string GeneralTopicId = "general";

    public static readonly IReadOnlyList<Topic> All = new List<Topic>
    {
        new(GeneralTopicId, "Overview",
            "Use the Dashboard tab to watch live input/output/CAN telemetry, and the Config " +
            "tab to change device settings.\n\n" +
            "Press F1 while hovering any panel to open help for that specific panel - any part " +
            "of the panel works, not just a particular control.\n\n" +
            "See the \"Device Overview\" topic in the list on the left for a rundown of every " +
            "channel the hardware has and what it's for."),

        new("device.overview", "Device Overview",
            "This app supports two PT Motorsport IO expander hardware variants, identified " +
            "automatically when you connect. Full wiring pinouts for each are below (from the " +
            "PT Motorsport quick reference wiring guides).\n\n" +
            "=== IO Mini2 (8-channel) ===\n\n" +
            "Digital Inputs - two 5-pin headers: D1,D2,D3,D4,0V and D5,D6,D7,D8,0V. 0-12V " +
            "tolerant; report HIGH/LOW state plus 0-100% duty cycle and 0-20,000Hz frequency. " +
            "The 0V pin on each header is the sensor ground for that header's inputs.\n\n" +
            "Analog Inputs - two 6-pin headers: Sensor GND,A1,A2,A3,A4,+5V and Sensor " +
            "GND,A5,A6,A7,A8,+5V. 0-5V inputs, 12V tolerant.\n\n" +
            "Digital Pulsed Outputs - two 4-pin \"OUT\" headers: OUT1-4 = DPO1,DPO2,DPO3,DPO4 " +
            "and OUT5-8 = DPO5,DPO6,DPO7,DPO8. 0-100% duty, 0-20,000Hz, 500mA limit per output, " +
            "low-side switched.\n\n" +
            "CAN/Power connector (4-pin): L = CAN Lo, H = CAN Hi, G = GND, 12 = +12V switched.\n\n" +
            "USB-C port - direct connection to the companion app / for firmware flashing.\n\n" +
            "Onboard solder pads (advanced, not part of the wiring harness):\n" +
            "- SWD pads: debug/flash via a Tag-Connect TC2030-MCP-NL probe.\n" +
            "- SDA/SCL pads: I2C lines.\n" +
            "- MD pad: boot mode select at reset (open = normal, bridged = bootloader).\n" +
            "- Reset pad: reset mode select (open = normal, bridged = bootloader).\n" +
            "- LED pad: open this bridge to disconnect the onboard diagnostic LED.\n" +
            "- NEO pad: onboard NeoPixel output, for extending an LED strip.\n" +
            "- An1-An8 Pullup pads: bridge with solder to enable a 1k pull-up on that analog " +
            "input.\n" +
            "- 120ohm CAN termination pad: bridge with solder to enable the bus terminating " +
            "resistor.\n" +
            "- VCC test pad: 12V (protected, ~0.3V under battery voltage), test point only.\n" +
            "- 3V3 test pad: 3.3V output, 20mA max, test point only.\n" +
            "- AREF pad (advanced): open = 5V reference from the MCU, bridged = reference from " +
            "the connector.\n\n" +
            "=== IO DTM2 (4-channel) ===\n\n" +
            "Digital Pulsed Outputs - two 3-pin connectors: DPO1,DPO2,VCC/+12V and " +
            "DPO3,DPO4,VCC/+12V. 3A limit per output, 7A total limit, low-side switched, fully " +
            "independent 0-20kHz frequency per output. DPOs can also be used as 0-30V analog " +
            "voltage-sense inputs.\n\n" +
            "Analog/Digital Input connector (two 6-pin rows on the same block):\n" +
            "- AVI1,AVI2,AVI3,AVI4,+5V,Sensor GND - 0-5V analog inputs.\n" +
            "- DI1,DI2,DI3,DI4,+5V,Sensor GND - digital/frequency inputs, 0-20,000Hz; can also " +
            "be used as AVIs.\n\n" +
            "CAN/Power connector: CAN H, CAN L, VCC/+12V, GND.\n\n" +
            "Onboard indicators (LEDs, not wiring pins): CAN activity light, power indicator " +
            "light.\n\n" +
            "Note: this is a text transcription of the PT Motorsport wiring diagrams, not the " +
            "diagrams themselves - check the official wiring guide/datasheet for the full " +
            "pictures and exact connector part numbers."),

        new("dashboard.analog", "Dashboard - Analog Input Information",
            "Live analog input voltages (0-5V, measured on a 14-bit ADC).\n\n" +
            "DTM2 only: also shows DPO1-4 output voltage sense (0-30V), on hardware/firmware " +
            "that supports it."),

        new("dashboard.digitalIn", "Dashboard - Digital Input Information",
            "Each row reads: DIn - STATE - Duty% - Freq Hz.\n\n" +
            "STATE is the input's current electrical level (HIGH/LOW). Duty%/Freq are measured " +
            "from the incoming signal's edges and show N/A if the signal isn't toggling."),

        new("dashboard.digitalOut", "Dashboard - Digital Output Information",
            "Each DPO (digital power output) row reads: DPOn - STATE - Duty% - Freq Hz.\n\n" +
            "STATE is ACTIVE when the output is actually driving current, INACTIVE when it is " +
            "not. This factors in the Active polarity setting from the Config tab, not just the " +
            "raw commanded duty - e.g. with Active unchecked (active-low) and duty at 0%, the " +
            "output is still physically ON.\n\n" +
            "If a value shows a bracket like \"(CAN: 80%)\", this channel is locked to the app's " +
            "value, and the bracketed number is what the CAN bus is currently requesting (which " +
            "has no effect while locked)."),

        new("dashboard.can", "Dashboard - CAN Information",
            "CAN Speed/Mode/TX Rate/IDs/RX Timeout mirror the Config tab's CAN Settings.\n\n" +
            "Safe State Active: YES when the device has not received a CAN frame within RX " +
            "Timeout and has forced every non-Duty-locked output to its configured Safe level.\n\n" +
            "RX Count / TX Count: total CAN frames received/transmitted since power-up."),

        new("config.digitalIn", "Config - Digital Inputs",
            "Input Pull up: enables the device's internal pull-up resistor for that digital " +
            "input channel.\n\n" +
            "Analog Input Pull up (1k) (DTM2 only): enables an external 1k pull-up on AV1-4.\n\n" +
            "DI State Debounce: minimum time (ms) a digital input must hold a new level before " +
            "the reported state changes, to filter electrical noise/bounce."),

        new("config.digitalOut", "Config - Output Settings",
            "Each DPO row controls:\n\n" +
            "Safe - the level this output is forced to while the device is in Safe State (CAN " +
            "communication lost/timed out). Checked = driven ON while in Safe State; unchecked " +
            "= driven OFF. Only takes effect while Safe State is actually active (see the " +
            "readout above the table) and only when this channel's Duty Lock is off.\n\n" +
            "Active - output drive polarity. High = commanded \"on\" drives the pin high; " +
            "Low = commanded \"on\" drives the pin low. This flips which physical voltage " +
            "level corresponds to an \"on\" output.\n\n" +
            "Important: this device only switches low-side (it can pull its output pin to " +
            "ground). Setting Active to \"High\" does not add a source of voltage - it just " +
            "inverts the pin's logic, and actually driving the load High-side requires an " +
            "external pull-up resistor from the output to the switched supply voltage. Most " +
            "loads (relays, solenoids, grounded-side devices) should use Low.\n\n" +
            "D.Lck/F.Lck/S.Lck/A.Lck - locks this channel's Duty/Frequency/Safe level/Active " +
            "polarity to the app's value shown. While locked, CAN messages cannot change that " +
            "parameter for that channel. Locking only ever happens when you tick a Lck box " +
            "yourself - editing Hz/%/Safe/Active no longer locks it automatically.\n\n" +
            "Hz/% - the configured frequency and duty cycle.\n\n" +
            "State - the live ACTIVE/INACTIVE status (same meaning as the Dashboard). Hover a " +
            "row's State cell for its current duty/frequency and what CAN is currently " +
            "requesting."),

        new("config.can", "Config - CAN Settings",
            "CAN Speed - bus bitrate (kbps); must match the rest of the CAN network.\n\n" +
            "CAN TX Base / RX Base - hex CAN IDs this device starts transmitting from / " +
            "listening from.\n\n" +
            "CAN TX Rate - how often (Hz) this device broadcasts its own status/telemetry " +
            "frames.\n\n" +
            "RX Timeout - how long (ms) without a received CAN frame before the device " +
            "considers CAN communication lost and enters Safe State.\n\n" +
            "CAN Mode - selects which ECU's CAN protocol this device decodes (Haltech, Emtron, " +
            "ECU Master, Motec, or PT default). Changing mode resets each channel's " +
            "Duty/Freq/Safe/Active lock to that mode's defaults."),

        new("config.reset", "Config - Default Button",
            "Resets all configuration (CAN settings, output settings, pull-ups, debounce) back " +
            "to factory defaults."),
    };

    public static Topic Find(string? id)
    {
        return All.FirstOrDefault(t => t.Id == id) ?? All[0];
    }
}
