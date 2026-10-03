Components:
- Gateway Server
	- Standalone backend server
- Data Center Server
	- Standalone backend server
	- Database Connection
- Game Server
	- Headless game engine server
- Game Client
	- Game engine client

Example Unity Architecture
- Gateway Server
	- Standalone .NET backend app
	- Primarily HTTP API
- Data Center Server
	- Standalone .NET backend app
	- PostgreSQL database
	- RPC API (gRPC or ActualLab.Rpc)
- Game World Server
	- Headless Unity engine server
- Game Client
	- Unity engine client

Data Center Database Structure
- Gameplay
	- Character
- Content
	- Zone
- Administration
	- User
	- Server