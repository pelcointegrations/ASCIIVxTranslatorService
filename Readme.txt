----------------------------------------------------
1/13/2017 Version 1.12.5.0 (To be used with VideoXpert 1.12 and up)

	Cleaned up code including comments.
	Removed unnecessary nuget packages.
	VXINT-758, Printed out "Received Command: " string regardless of DebugLevel.
	
----------------------------------------------------
1/11/2017 Version 1.12.4.0 (To be used with VideoXpert 1.12 and up)

	Added logic to occasionally (every 3 - 5 minutes) update Serenity resources.  Previously monitors
	or cameras that were added to the system would not be detected unless the service was restarted.

----------------------------------------------------
1/10/2017 Version 1.12.3.0 (To be used with VideoXpert 1.12 and up)

	Fixed issue with InsertText not being present in command file.  Command interpreter
	was throwing exception when this command was not found.  Since InsertText is not supported
	the code was removed that looks for this command.
	
	Added support for special character '~' before Iris, Focus, and PTZ commands.  Tested with
	KBD300A (with switches 5 and 8 ON).
		
	Added thread to send PTZ commands every 50ms so that serial commands are able to write
	over last command.  Only the latest command at the interval will be sent.  This speeds
	up processing by throwing out irrelevant commands (only latest matters).
	
	When you select a monitor or a cell now, it looks up the camera on the cell so that
	user does not have to enter camera number before using PTZ commands.
	
----------------------------------------------------
12/20/2016 Version 1.12.2.0 (To be used with VideoXpert 1.12 and up)

	Fixed VXINT-731.  Zoom In and Zoom Out were reversed.

----------------------------------------------------
12/20/2016 Version 1.12.1.0 (To be used with VideoXpert 1.12 and up)

	Fixed VXINT-698.  Upgrading was leaving duplicate entries in Programs and Features.  Found that Wix does
	not uninstall if the only the last digit is changed.

----------------------------------------------------
12/19/2016 Version 1.12.0.5 (To be used with VideoXpert 1.12 and up)
	
	Fixed issue with registering integration as an external device.  IntegrationId in settings file
	is no longer used.  Core assigns an Id when registering and we read the id back and use that as
	the integrationId.
	
	Updated VxSDK (tagged "ASCII_Translator_1.12.0.5"), fixed device enumerations.

----------------------------------------------------
12/14/2016 Version 1.12.0.4 (To be used with VideoXpert 1.12 and up)
	
	Updated VxSDK (tagged "ASCII_Translator_1.12.0.4") to master branch which is now merged with 
	changes from MonitorSupport Branch.

----------------------------------------------------
12/06/2016 Version 1.12.0.3 (To be used with VideoXpert 1.12 and up)
	Added LineDelimiter option to POSSettings to inject partial receipt into Vx at defined
	LineDelimiter.
	
	TESTFILE command now interprets each line and waits 1 - 5 seconds before reading in the
	next line to more closely simulate a receipt being received and injected in parts.
	
----------------------------------------------------
11/29/2016 Version 1.12.0.2 (To be used with VideoXpert 1.12 and up)
	Added POSSettings to ASCIIEventServerSettings.xml to allow for POS (point of sale) mode.
	POSMode will look for Point Of Sale data, when matched an Event (Alarm Number 1 with AlarmState 1
	defined in AlarmConfiguration.xml) will be injected into VideoXpert.  Normal ASCII commands will not work
	in this mode.

	Added TESTFILE command line command (usage "TESTFILE filename.txt") to allow for automated command
	testing.  Each character is read from the file and sent to the interpreter.

	Updated VxSDK (tagged "ASCII_Translator_1.12.0.2") to up property value char limit from 64.

----------------------------------------------------
11/22/2016 Version 1.12.0.1 (To be used with VideoXpert 1.12 and up)
	Added SetCameraLayout, Play, Stop, Pause, FastForward, Rewind, Seek and ToggleLive commands.

	Added comments to defaultASCIICommandConfiguration.xml file.

	Added command line debug command to list all monitors (MONITOR)

	Updated VXSDK (tagged "ASCII_Translator_1.12.0.1")

----------------------------------------------------
11/09/2016 Version 1.12.0.0 (To be used with VideoXpert 1.12 and up)
	Replaced VxWrapper with VxSDK implementation built with VS 2015, .NET 4.61, C++ v140 toolset.
	VxSDK built using MonitorSupport branch tagged with "ASCII_Translator_1.12.0.0".

	Monitor commands now control Shared Display in Ops Center, not Collab Tab.

	Added IntegrationId to defaultASCIIEventServerSettings.xml.  This is the unique identifier
	used by the ASCII integration to identify itself to VideoXpert.  A unique guid should be
	used.  If an IntegrationId is not defined, the service uses a default integrationId
	of "E024457E-B2A2-49B5-AE62-20816418650F".  After the service starts the integration Id
	and "ASCII Vx Translator Service" should appear in the Devices tab of the Admin Portal.
	
	Added VxCorePort to defaultASCIIEventServerSettings.xml.  This element allows the user
	to define the port Vx communicates on.  Default is 443.
	
	Removed GeneratorDeviceId from defaultAlarmConfiguration.xml.  IntegrationId from
	defaultASCIIEventServerSettings is now used to fill in the GeneratorDeviceId when
	sending an event.
	
	When the keyword "USE_INTEGRATION_ID" string is used in the SourceDeviceId element of
	AlarmConfiguration.xml, this now tells the service to fill in the SourceDeviceId with
	the IntegrationId from the ASCIIEventServerSettings.  Leaving the SourceDeviceId blank
	will also result in the IntegrationId being filled in for the SourceDeviceId. 
	
	Changed defaultCustomSituations.xml and defaultAlarmConfiguration.xml to reflect using
	external situations (preferred over using internal situations for integration purposes).
	
	Removed commands from defaultASCIICommandConfiguration.xml that are not implemented.
	
	Removed defaultCameraConfiguration.xml and defaultMonitorConfiguration.xml.  These files
	are no longer needed as the monitor and camera numbers may be assigned in Admin Portal or
	the Ops Center in VideoXpert 1.12.

	
----------------------------------------------------

NOTE: version 1.0.9.0 and lower of the ASCIITranslatorService should be used with VideoXpert 1.09 - 1.11.
Monitor commands in these versions control the Collab Tab.

----------------------------------------------------
10/27/2016 Version 1.0.9.0
	VXINT-539, if a pattern (or preset) can't be found, update list of patterns by calling RefreshPresetsAndPatterns

----------------------------------------------------
10/26/2016 Version 1.0.8.0
	VXINT-539, Fixed issue with running a pattern - was calling preset rather than pattern.
	
	VXINT-541, Added debug messages when Wait command is in effect and commands are not being processed.  
	Enforced wait on commands entered from console (previously not enforced).

----------------------------------------------------
10/07/2016 Version 1.0.7.0
	Code now attempts to recover if connection to VideoXpert system is lost or serial port connection is lost.

----------------------------------------------------
10/06/2016 Version 1.0.6.0
	Added support for patching custom situations in Vx based on difference in custom situations in our xml.

----------------------------------------------------
9/26/2016 Version 1.0.5.0
	Added support for custom situations to be added via CustomSitations.xml to Vx system.
	Added Console command to list SITUATIONS. (Can provide string as filter for list)

----------------------------------------------------
9/23/2016 Version 1.0.4.0
	Added console helper command "datasource".

----------------------------------------------------
9/23/2016 Version 1.0.3.0
	Stripped \n \r from end of username and password - possible issue with base64 encode or decode.
	Prevent null reference on camera preset or pattern retrieval.

----------------------------------------------------
9/12/2016 Version 1.0.2.0
	Updated to support alarm enable and alarm clear situations.  Logged to vx.  
	Tested cmdline and COM port via glass keyboard.
	Additional Commands supported:
		TriggerAlarm
		ClearAlarm
----------------------------------------------------
9/6/2016 Version 1.0.1.0
	Updated tested and corrected issue with COM port support from v1.0.0.
----------------------------------------------------
9/1/2016 Version 1.0.0.0
	Initial Version (untested through COM port or Ethernet)
	Commands supported
		SelectMonitor
		SelectCell
		SelectCamera
		NextCamera
		PreviousCamera
		SingleCameraMode
		CameraMode2x2
		CameraMode3x3
		CameraMode4x4
		GotoPreset
		ExecutePattern
		Wait
		
	Able to launch commands from debug console (start application from windows explorer) using 
	"cmd ASCII cmd". For example to Select Monitor 1 enter text "cmd 1Ma" with default commands.
	
	More than one command can be entered at a time.  For example, Select Monitor 1 and 
	Select Camera 1 = "cmd 1Ma1#a" or "cmd 1Ma" and "cmd 1#a".