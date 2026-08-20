-- Spawns the known DedSec shop archetype at the reticle hit location.
local l=GetReticleHitLocation()
SpawnEntityFromArchetype('{5991467D-8E99-431F-AE1B-724D46EDE1E9}',l[1],l[2],l[3],0,0,180+GetEntityAngle(GetLocalPlayerEntityId(),2))
