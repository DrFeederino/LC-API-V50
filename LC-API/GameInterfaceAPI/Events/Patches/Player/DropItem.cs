using GameNetcodeStuff;
using HarmonyLib;
using LC_API.GameInterfaceAPI.Events.EventArgs.Player;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LC_API.GameInterfaceAPI.Events.Patches.Player
{
    [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DiscardHeldObject))]
    internal class DroppingItem
    {
        internal static DroppingItemEventArgs CallEvent(PlayerControllerB playerController, bool placeObject, Vector3 targetPosition,
            int floorYRotation, NetworkObject parentObjectTo, bool matchRotationOfParent, bool droppedInShip)
        {
            if (Plugin.configVanillaSupport.Value) return null;

            Features.Player player = Features.Player.GetOrAdd(playerController);

            Features.Item item = Features.Item.GetOrAdd(playerController.currentlyHeldObjectServer);

            DroppingItemEventArgs ev = new DroppingItemEventArgs(player, item, placeObject, targetPosition, floorYRotation, parentObjectTo, matchRotationOfParent, droppedInShip);

            Handlers.Player.OnDroppingItem(ev);

            player.CallDroppingItemOnOtherClients(item, placeObject, targetPosition, floorYRotation, parentObjectTo, matchRotationOfParent, droppedInShip);

            return ev;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> newInstructions = new List<CodeInstruction>(instructions);

            // original function was changed to have "!base.IsOwner" stuff with return, so that should be first, otherwise the function will call itself => infinite recursive.
            int firstRetun = newInstructions.FindIndex(i => i.opcode == OpCodes.Ret) + 1;
            int animIndex = newInstructions.FindIndex(i => i.opcode == OpCodes.Ldarg_1) - firstRetun;

            CodeInstruction[] animatorStuff = newInstructions.GetRange(firstRetun, animIndex).ToArray();

            newInstructions.RemoveRange(firstRetun, animIndex);

            LocalBuilder isInShipLocal = generator.DeclareLocal(typeof(bool));

            // Dr Feederino: this is the first "hook" of CallEvent() call into the PlayerControllerB.DiscardHeldObject(). It is done at the first Stloc_0, basically at the very beginning of the function before anything happens so it "can" control the flow of the original code.
            // Dr Feederino: V60 has another update of DiscardHeldObject
            // Dr Feederino: V64 yet another update in locals of DiscardHeldObject
            {
                const int offset = 1;

                int index = newInstructions.FindIndex(i => i.opcode == OpCodes.Stloc_0) + offset;

                Label nullLabel = generator.DefineLabel();
                Label notAllowedLabel = generator.DefineLabel();
                Label skipLabel = generator.DefineLabel();

                CodeInstruction[] inst = new CodeInstruction[]
                {
                    // DroppingItemEventArgs ev = DroppingItem.CallEvent(PlayerControllerB, bool, Vector3, 
                    //  int, NetworkObject, bool, bool)
                    new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(newInstructions[index]), // "this"
                    new CodeInstruction(OpCodes.Ldarg_1), // PlayerControllerB
                    new CodeInstruction(OpCodes.Ldarg_3), // bool
                    new CodeInstruction(OpCodes.Ldloc, 4), // vector3
                    new CodeInstruction(OpCodes.Ldarg_2), // int
                    new CodeInstruction(OpCodes.Ldarg, 4), // networkobject
                    new CodeInstruction(OpCodes.Ldarg_0), // bool
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(PlayerControllerB), nameof(PlayerControllerB.isInHangarShipRoom))), // bool
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DroppingItem), nameof(DroppingItem.CallEvent))),

                    // if (ev is null) -> base game code
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Brfalse_S, nullLabel),

                    new CodeInstruction(OpCodes.Dup),

                    // if (!ev.IsAllowed) return
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.IsAllowed))),
                    new CodeInstruction(OpCodes.Brfalse_S, notAllowedLabel),

                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Dup),

                    // placePosition = ev.TargetPosition
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.TargetPosition))),
                    new CodeInstruction(OpCodes.Starg, 3),

                    // floorYRot2 = ev.FloorYRotation
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.FloorYRotation))),
                    new CodeInstruction(OpCodes.Stloc, 2),

                    // parentObjectTo = ev.ParentObjectTo
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.ParentObjectTo))),
                    new CodeInstruction(OpCodes.Starg, 2),

                    // matchRotationOfParent = ev.MatchRotationOfParent
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.MatchRotationOfParent))),
                    new CodeInstruction(OpCodes.Starg, 4),

                    // droppedInShip = ev.DroppedInShip
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.DroppedInShip))),
                    new CodeInstruction(OpCodes.Stloc, isInShipLocal.LocalIndex),
                };

                inst = inst.AddRangeToArray(animatorStuff);

                inst = inst.AddRangeToArray(new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Br, skipLabel),
                    new CodeInstruction(OpCodes.Pop).WithLabels(nullLabel),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(PlayerControllerB), nameof(PlayerControllerB.isInHangarShipRoom))),
                    new CodeInstruction(OpCodes.Stloc, isInShipLocal.LocalIndex),
                });

                inst = inst.AddRangeToArray(animatorStuff);

                inst = inst.AddRangeToArray(new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Br, skipLabel),
                    new CodeInstruction(OpCodes.Pop).WithLabels(notAllowedLabel),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldc_I4_0),
                    new CodeInstruction(OpCodes.Stfld, AccessTools.Field(typeof(PlayerControllerB), nameof(PlayerControllerB.throwingObject))),
                    new CodeInstruction(OpCodes.Ret)
                });

                newInstructions.InsertRange(index, inst);

                newInstructions[index + inst.Length].labels.Add(skipLabel);

                // Dr Feederino: find the index of the first call of SetObjectAsNoLongerHeld() where the arguements would be declared and passed.
                // The body of original method can vary and change, so this is very probable to break from update to update.
                int firstMethodCallIndex = newInstructions.FindIndex(i => i.Calls(AccessTools.Method(typeof(PlayerControllerB), nameof(PlayerControllerB.SetObjectAsNoLongerHeld))));

                // Dr Feederino notes:
                // Idea is to remove the following stuff before SetObjectAsNoLongerHeld().
                // These 4 lines declare that need to load from this.isInElevator and this.isInHangarShipRoom (ldarg.0 = "this" aka current instance of object), and pass them as first two arguements to GameNetcodeStuff.PlayerControllerB::SetObjectAsNoLongerHeld(System.Boolean,System.Boolean,UnityEngine.Vector3,GrabbableObject,System.Int32)
                // Huge note: the first ldarg.0 is REQUIRED because this method is called against INSTANCE of the object, hence, it is required.
                // IL_0531: ldarg.0
                // IL_0532: ldfld System.Boolean GameNetcodeStuff.PlayerControllerB::isInElevator
                // IL_0537: ldarg.0
                // IL_0538: ldfld System.Boolean GameNetcodeStuff.PlayerControllerB::isInHangarShipRoom

                // This is to replace them (isInElevator and isInHangarShipRoom) with declared locally IsInShip bool variable aka droppedInShip in DroppingItemEventArgs.

                // Subtraction of 8 is required to go the first "IL_0531", 4 is to remove lines explained above.
                newInstructions.RemoveRange(firstMethodCallIndex - 8, 4);

                // Now it is required to insert two lines that expose locally declared IsInShip bool for first two arguements.
                newInstructions.InsertRange(firstMethodCallIndex - 8, new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Ldloc, isInShipLocal.LocalIndex),
                    new CodeInstruction(OpCodes.Ldloc, isInShipLocal.LocalIndex)
                });
            }

            // Dr Feederino: this is the second "hook" of CallEvent() call into the PlayerControllerB.DiscardHeldObject(). It is done at the last Stloc_S, before the second call SetObjectAsNoLongerHeld()
            {
                const int offset = 1;

                int index = newInstructions.FindLastIndex(i => i.opcode == OpCodes.Stloc_S) + offset;

                Label nullLabel = generator.DefineLabel();
                Label notAllowedLabel = generator.DefineLabel();
                Label skipLabel = generator.DefineLabel();

                CodeInstruction[] inst = new CodeInstruction[]
                {
                    // DroppingItemEventArgs ev = DroppingItem.CallEvent(PlayerControllerB, bool, Vector3, 
                    //  int, NetworkObject, bool, bool)
                    new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(newInstructions[index]),  // playerController (this)
                    new CodeInstruction(OpCodes.Ldarg_1),  // placeObject 
                    new CodeInstruction(OpCodes.Ldloc_S, 3),  // placePosition (targetPosition V_5)
                    new CodeInstruction(OpCodes.Ldloc_S, 2),  // floorYRotation (V_4)
                    new CodeInstruction(OpCodes.Ldarg_2), // parentObjectTo
                    new CodeInstruction(OpCodes.Ldarg, 4), // matchRotationOfParent 
                    new CodeInstruction(OpCodes.Ldloc_S, 4),// droppedInShip (V_6)
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DroppingItem), nameof(DroppingItem.CallEvent))),

                    // if (ev is null) -> base game code
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Brfalse_S, nullLabel),

                    new CodeInstruction(OpCodes.Dup),

                    // if (!ev.IsAllowed) return
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.IsAllowed))),
                    new CodeInstruction(OpCodes.Brfalse_S, notAllowedLabel),

                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Dup),

                    // targetFloorPosition = ev.TargetPosition
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.TargetPosition))),
                    new CodeInstruction(OpCodes.Stloc_S, 3),

                    // floorYRot = ev.FloorYRotation
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.FloorYRotation))),
                    new CodeInstruction(OpCodes.Stloc_S, 2),

                    // parentObjectTo = ev.ParentObjectTo
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.ParentObjectTo))),
                    new CodeInstruction(OpCodes.Starg, 2),

                    // matchRotationOfParent = ev.MatchRotationOfParent
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.MatchRotationOfParent))),
                    new CodeInstruction(OpCodes.Starg, 4),

                    // droppedInShip = ev.DroppedInShip
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(DroppingItemEventArgs), nameof(DroppingItemEventArgs.DroppedInShip))),
                    new CodeInstruction(OpCodes.Stloc, isInShipLocal.LocalIndex),
                };

                inst = inst.AddRangeToArray(animatorStuff);

                inst = inst.AddRangeToArray(new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Br, skipLabel),
                    new CodeInstruction(OpCodes.Pop).WithLabels(nullLabel),
                    new CodeInstruction(OpCodes.Ldloc_S, 4),
                    new CodeInstruction(OpCodes.Stloc, isInShipLocal.LocalIndex),
                });

                inst = inst.AddRangeToArray(animatorStuff);

                inst = inst.AddRangeToArray(new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Br, skipLabel),
                    new CodeInstruction(OpCodes.Pop).WithLabels(notAllowedLabel),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldc_I4_0),
                    new CodeInstruction(OpCodes.Stfld, AccessTools.Field(typeof(PlayerControllerB), nameof(PlayerControllerB.throwingObject))),
                    new CodeInstruction(OpCodes.Ret)
                });

                newInstructions.InsertRange(index, inst);

                newInstructions[index + inst.Length].labels.Add(skipLabel);

                // Dr Feederino: shenanigans with finding index of second call to SetObjectAsNoLongerHeld() is not needed as the index of last opcode_s correctly points right before the call and the code below is valid.
                newInstructions.RemoveRange(index + inst.Length + 1, 3);

                newInstructions.InsertRange(index + inst.Length + 1, new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Ldloc, isInShipLocal.LocalIndex),
                    new CodeInstruction(OpCodes.Ldloc, isInShipLocal.LocalIndex)
                });
            }

            for (int i = 0; i < newInstructions.Count; i++) yield return newInstructions[i];
        }
    }
}