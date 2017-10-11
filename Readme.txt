----------------------------------------------------
8/10/2017 Version 2.0.9.0

	Allow USE_INTEGRATION_ID to fill in SourceDeviceId for CustomSituations much
	like it does for Event Injection itself.
	
----------------------------------------------------
8/10/2017 Version 2.0.8.0

	VXINT-1123 Added SourceDeviceId to CustomSituations.xml.
	
----------------------------------------------------
8/9/2017 Version 2.0.7.0

	VXINT-1108.  Fixed random crash that was only occurring when configured for TCP.
	As part of this, TCPListener class is not longer being used in favor of Sockets.
	Also Platform Target changed to X86 to follow VXSdk settings.

	For test purposes, added ability to call a script directly from console.
	Typing "cmd SCRIPT1" will now execute script 1.  "cmd SCRIPT2" will execute 
	script 2, etc.

----------------------------------------------------
8/1/2017 Version 2.0.6.0

	Added Response configuration to Command file.  If defined Ack and Nack will return 
	the defined string as a response to a valid/invalid command respectively.  
	Ack does not mean the command was successful, it just means that a valid command 
	was successfully received and will be attempted.
	
	Added KeepAlive command to Command file.  This command can be used in conjunction
	with the Response definitions to provide a means of keep alive.  This command
	does nothing other than return the defined Ack value.
	
	Responses are valid for serial communications and TCP.  Not UDP.  Note that
	TCP will return the defined Ack value or the default return values for
	TCP (AcK and NacK).

----------------------------------------------------
7/26/2017 Version 2.0.5.0

	Updated to VxSDK 2.0.  SHA-1: 12092cf6ba8a97a5e2e67f865045d19147a79ca5

	Fixed issue VXINT-1098.  Since the addition of sessions, PTZ handling has
	needed to be updated to ensure sessions do not interfere with each other in
	regard to PTZ data.  They can still affect one another in fighting over control
	of a PTZ, but now the data itself should be protected between sessions.
	
----------------------------------------------------
7/17/2017 Version 2.0.4.0 (To be used with VideoXpert 1.12 and up)

	VXINT-1089 Unable to map ASCII Monitor to VX Monitor/Cell.
	
	Issue occurred after changes in 2.0.1.0.  Reviewed all cell numbers 
	being used and ensured use of returned cell number from GetMonitor call.
	This routine performs mapping and may change the cell number
	which was not used consistently.

----------------------------------------------------
7/12/2017 Version 2.0.3.0 (To be used with VideoXpert 1.12 and up)

	VXINT-1080 StopPTZ was clearing camera number after setting it.
	
	Changed some of the debug output from script handling so that cell numbers are 
	consistent (report 1 based rather than 0 based).

----------------------------------------------------
6/23/2017 Version 2.0.2.0 (To be used with VideoXpert 1.12 and up)

	Added AcK and NacK response for TCP connection similar to UDI5000.
	Note that a single tcp message containing two commands, like "101Ma3#a" will
	result in two acknowledges coming back "AcKAcK" (1 for each command).
	
----------------------------------------------------
6/22/2017 Version 2.0.1.0 (To be used with VideoXpert 1.12 and up)

	Added desired feature to disconnect video from current cell if
	camera 0 is selected.  Note that in order to make this work, the
	SelectCamera command in the ASCIICommandConfiguration.xml file will
	need to have the minimum set to 0 ( <Min>0</Min> )
	
	Added sessions to keep track of input from various connection types.
	Each input type will now remember which monitor, camera and cell was 
	last assigned through that input.  Inputs are the console, serial port,
	UDP or TCP port.
	
	Added ability to accept TCP connections.  Connection will remain open
	until client disconnects.  A new XML tag has been added to
	ASCIIEventServerSettings.xml under EthernetSettings:
	<ConnectionType>UDP</ConnectionType> - listens for UDP
	<ConnectionType>TCP</ConnectionType> - listens for TCP
	<ConnectionType>TCP MultiSession</ConnectionType> - listens for TCP,
	each connection will have its own session

----------------------------------------------------
5/9/2017 Version 2.0.0.0 (To be used with VideoXpert 1.12 and up)

	VXINT-1002 Fixed issue with DisplayCamera when same camera is already
	in monitor cell and in playback.  Camera was not going to live.  Fix included
	from EventMonitor service.

	Upgraded VxSDK SHA-1: a484b146e3258b4fe41540f17212093c7857268c on gitlab Master
	which includes fix for a subscription issue where loginInfo was being lost and 
	subscribe call subsequently failing.

----------------------------------------------------
4/4/2017 Version 1.12.13.0 (To be used with VideoXpert 1.12 and up)

	Upgraded to VxSDK SHA-1: de6890fa4ceb7e4ad899730d41e901c1d042fcb1
	VXINT-961, Previous change for setting speed had unintended consequence of preventing
	disconnect from occurring.  If trying to disconnect video, no longer sending speed.

----------------------------------------------------
3/29/2017 Version 1.12.12.0 (To be used with VideoXpert 1.12 and up)

	Upgraded to VxSDK SHA-1: 4cfa56bc91897991ccbe6a39b02cebe833a76321.
	When setting the data source of a monitor cell, added setting speed of 1 to VxSDK
	to conform to Serenity spec.  Note that time is supposed to be set as well, but the
	SDK is unable to set a value to null and an empty string causes an exception.  VxSDK
	should be further enhanced to allow us to send a null for time in the future.
	
----------------------------------------------------
3/24/2017 Version 1.12.11.0 (To be used with VideoXpert 1.12 and up)

	Removed POS mode. This was primarily used as a demo.
	
	Removed AutoCellRotateTimeout.  This feature added complications to the code and was not
	used at the integration site it was intended for, where MonitorToCellMap functionality
	was used instead.
	
	Updated VxSDK to latest code on master 3/22/2017(SHA-1: 513375f14b97982808bae98d91d749b617ad1986).
	
	Added a system level check where if any of the three main data lists (monitor, situation 
	or datasources) are null or 0 length, we force a reconnect to the system.
	
	Removed multiple locks and revisted entire locking strategy now that system may be ripped out from
	under the code at any time - system, monitors, datasources and situations are now all locked
	under the system lock - they all use the same resource (vxsdk) anyway.
	
	Added BookMark Action to Scripts.	

----------------------------------------------------
3/15/2017 Version 1.12.10.0 (To be used with VideoXpert 1.12 and up)

	Added MonitorToCellMap functionality which lets a user map ASCII monitor numbers to a
	particular cell of a monitor.
	
----------------------------------------------------
3/15/2017 Version 1.12.9.0 (To be used with VideoXpert 1.12 and up)

	Added AutoCellRotateTimeout to configuration, allowing users to select a timeout in milliseconds
	for resetting to cell 1. If not 0, this will enable the autoCell selection feature which rotates
	the cell from 1 to max cells on screen.  The timeout sets it back to cell 1.  This is only checked
	on ASCII command SelectCamera.
	
	DebugLevel greater than two now prints all characters received through serial or ethernet.

----------------------------------------------------
2/21/2017 Version 1.12.8.0 (To be used with VideoXpert 1.12 and up)

	Added ability to call scripts on an alarm trigger or clear command.  Scripts are defined 
	in ASCIIScripts.xml file.  <ExecuteScript> element has been added to the <Situation>
	element of the AlarmConfiguration.  Change is in support of VXINT-859.
	
----------------------------------------------------
1/26/2017 (To be used with VideoXpert 1.12 and up)

	Changed Select Camera Layout Max value to 18 in defaultASCIICommandConfiguration.xml file.
	
	Updated VxSDK to released version 1.2.

----------------------------------------------------
1/19/2017 Version 1.12.7.0 (To be used with VideoXpert 1.12 and up)

	VXINT-763 Fixed issue with FF not working by updating to latest VxSDK.
	
	Removed parameter from Play command for consistency.  Selecting a camera must
	be done through SelectCamera command.
	
	Note: this change will cause and issue with the Play command from previous versions
	of the service.  The Play command in an existing command file should be replaced
	with the newer version or simply remove the Parameter element.
	
----------------------------------------------------
1/18/2017 Version 1.12.6.0 (To be used with VideoXpert 1.12 and up)

	Removed parameter from Pause command.  Pause operation will be on selected camera
	rather than allowing camera to be sent with command.
	
	Set default upper limit to FF and REW to 128.

	Note: this change will cause and issue with the Pause command from previous versions
	of the service.  The Pause command in an existing command file should be replaced
	with the newer version or simply remove the Parameter element.
	
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