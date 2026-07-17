using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PlayerUpdatePacketUpdatePositionHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PlayerUpdatePacketUpdatePositionHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
    {
        if (!PlayerUpdatePacketUpdatePosition.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PlayerUpdatePacketUpdatePosition));
            return false;
        }

        connection.Player.Mount?.UpdatePosition(packet.Position, packet.Rotation);

        if (connection.Player.Pet is not null)
        {
            var pet = connection.Player.Pet;

            var ownerMoveDx = packet.Position.X - pet.OwnerLastPosition.X;
            var ownerMoveDz = packet.Position.Z - pet.OwnerLastPosition.Z;
            var ownerMoveDistance = System.MathF.Sqrt(ownerMoveDx * ownerMoveDx + ownerMoveDz * ownerMoveDz);
            const float ownerMovementThreshold = 0.05f;
            bool ownerIsMoving = ownerMoveDistance > ownerMovementThreshold;

            pet.OwnerLastPosition = packet.Position;

            var rotation = packet.Rotation;
            var forward = new System.Numerics.Vector3(
                2.0f * (rotation.X * rotation.Z + rotation.W * rotation.Y),
                2.0f * (rotation.Y * rotation.Z - rotation.W * rotation.X),
                1.0f - 2.0f * (rotation.X * rotation.X + rotation.Y * rotation.Y)
            );

            const float followDistance = 3.0f;
            var targetPetPosition = new System.Numerics.Vector4(
                packet.Position.X - forward.X * followDistance,
                packet.Position.Y,
                packet.Position.Z - forward.Z * followDistance,
                packet.Position.W
            );

            var currentPetPosition = pet.Position;

            var dx = targetPetPosition.X - currentPetPosition.X;
            var dz = targetPetPosition.Z - currentPetPosition.Z;
            var distance = System.MathF.Sqrt(dx * dx + dz * dz);

            var ownerDx = packet.Position.X - currentPetPosition.X;
            var ownerDz = packet.Position.Z - currentPetPosition.Z;
            var distanceToOwner = System.MathF.Sqrt(ownerDx * ownerDx + ownerDz * ownerDz);

            const float teleportDistance = 20.0f;
            const float stopDistance = 1.5f;
            const float runDistance = 8.0f;
            const float idleRange = 6.0f;

            const float walkSpeed = 4.5f;
            const float runSpeed = 9.0f;
            const float smoothingFactor = 0.15f;

            System.Numerics.Vector4 newPetPosition;
            byte movementState;

            if (distance > teleportDistance)
            {
                newPetPosition = targetPetPosition;
                movementState = 0;

                _logger.LogDebug("Pet teleported to owner. Distance was {distance}", distance);
            }
            else if (!ownerIsMoving && distanceToOwner < idleRange)
            {
                newPetPosition = currentPetPosition;
                movementState = 0;
            }
            else if (distance < stopDistance)
            {
                newPetPosition = currentPetPosition;
                movementState = 0;
            }
            else
            {
                var speed = distance > runDistance ? runSpeed : walkSpeed;
                movementState = distance > runDistance ? (byte)2 : (byte)1;

                newPetPosition = new System.Numerics.Vector4(
                    currentPetPosition.X + dx * smoothingFactor * (speed / walkSpeed),
                    targetPetPosition.Y,
                    currentPetPosition.Z + dz * smoothingFactor * (speed / walkSpeed),
                    currentPetPosition.W
                );
            }

            var petRotation = rotation;
            if (movementState > 0 && distance > 0.1f)
            {
                var angle = System.MathF.Atan2(dx, dz);
                var halfAngle = angle / 2.0f;
                petRotation = new System.Numerics.Quaternion(
                    0,
                    System.MathF.Sin(halfAngle),
                    0,
                    System.MathF.Cos(halfAngle)
                );
            }

            pet.UpdatePosition(newPetPosition, petRotation);

            if (movementState != pet.Animation)
            {
                var newSpeed = movementState switch
                {
                    0 => 0f,
                    1 => walkSpeed,
                    2 => runSpeed,
                    _ => walkSpeed
                };

                pet.Speed = newSpeed;

                var speedPacket = new PlayerUpdatePacketExpectedSpeed
                {
                    Guid = pet.Guid,
                    ExpectedSpeed = newSpeed
                };

                connection.Player.SendTunneledToVisible(speedPacket, true);
            }

            var lastSentPos = pet.LastSentPosition;
            var sendDx = newPetPosition.X - lastSentPos.X;
            var sendDz = newPetPosition.Z - lastSentPos.Z;
            var sendDistance = System.MathF.Sqrt(sendDx * sendDx + sendDz * sendDz);

            const float minSendDistance = 0.1f;
            if (sendDistance >= minSendDistance || movementState != pet.Animation)
            {
                var petUpdate = new PlayerUpdatePacketUpdatePosition
                {
                    Guid = pet.Guid,
                    Position = newPetPosition,
                    Rotation = petRotation,
                    State = movementState,
                    Unknown = packet.Unknown
                };

                pet.LastSentPosition = newPetPosition;
                pet.Animation = movementState;
                connection.Player.SendTunneledToVisible(petUpdate, true);
            }
        }

        connection.Player.UpdatePosition(packet.Position, packet.Rotation);

        connection.Player.SendTunneledToVisible(packet);

        return true;
    }
}
