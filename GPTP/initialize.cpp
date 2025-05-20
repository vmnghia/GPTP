#include "Plugin.h"
#include "definitions.h"
#include "file_data.h"
#include "hook_tools.h"
using namespace dll;

// Hook header files
#include "graphics/draw_hook.h"
#include "hooks/main/game_hooks.h"

#include "hooks/apply_upgrade_flags.h"
#include "hooks/attack_priority.h"
#include "hooks/bunker_hooks.h"
#include "hooks/cloak_nearby_units.h"
#include "hooks/cloak_tech.h"
#include "hooks/detector.h"
#include "hooks/harvest.h"
#include "hooks/rally_point.h"
#include "hooks/recharge_shields.h"
#include "hooks/stim_packs.h"
#include "hooks/tech_target_check.h"
#include "hooks/transfer_tech_upgrades.h"
#include "hooks/unit_speed.h"
#include "hooks/update_status_effects.h"
#include "hooks/update_unit_state.h"
#include "hooks/weapons/weapon_cooldown.h"
#include "hooks/weapons/weapon_damage.h"
#include "hooks/weapons/weapon_fire.h"

#include "hooks/psi_field.h"
#include "hooks/unit_destructor_special.h"

#include "hooks/interface/status_display/weapon_armor_tooltip.h"
#include "hooks/unit_stats/armor_bonus.h"
#include "hooks/unit_stats/max_energy.h"
#include "hooks/unit_stats/sight_range.h"
#include "hooks/unit_stats/weapon_range.h"

// in alphabetical order
#include "hooks/attack_and_cooldown.h"
#include "hooks/cheat_codes.h"
#include "hooks/create_init_units.h"
#include "hooks/give_unit.h"
#include "hooks/interface/btns_cond.h"
#include "hooks/interface/buttonsets.h"
#include "hooks/interface/select_larva.h"
#include "hooks/interface/selection.h"
#include "hooks/interface/status_display/advanced/status_base_text.h"
#include "hooks/interface/status_display/advanced/status_buildmorphtrain.h"
#include "hooks/interface/status_display/advanced/status_nukesilo_resources.h"
#include "hooks/interface/status_display/advanced/status_research_upgrade.h"
#include "hooks/interface/status_display/advanced/status_supply_provider.h"
#include "hooks/interface/status_display/advanced/status_transport.h"
#include "hooks/interface/status_display/stats_display_main.h"
#include "hooks/interface/status_display/stats_panel_display.h"
#include "hooks/interface/status_display/unit_portrait.h"
#include "hooks/interface/status_display/unit_stat_act.h"
#include "hooks/interface/status_display/unit_stat_cond.h"
#include "hooks/interface/status_display/unit_stat_selection.h"
#include "hooks/interface/status_display/wireframe.h"
#include "hooks/interface/updateSelectedUnitsData.h"
#include "hooks/load_unload_proc.h"
#include "hooks/orders/0_orders/orders_root.h"
#include "hooks/orders/base_orders/attack_orders.h"
#include "hooks/orders/base_orders/die_orders.h"
#include "hooks/orders/base_orders/move_orders.h"
#include "hooks/orders/base_orders/patrol_order.h"
#include "hooks/orders/base_orders/rightclick_order.h"
#include "hooks/orders/base_orders/stopholdpos_orders.h"
#include "hooks/orders/building_making/building_morph.h"
#include "hooks/orders/building_making/building_protoss.h"
#include "hooks/orders/building_making/building_terran.h"
#include "hooks/orders/building_making/make_nydus_exit.h"
#include "hooks/orders/burrow_orders.h"
#include "hooks/orders/cloak_nearby_units_order.h"
#include "hooks/orders/critter_order.h"
#include "hooks/orders/doodad_orders.h"
#include "hooks/orders/enter_nydus.h"
#include "hooks/orders/harvest_orders.h"
#include "hooks/orders/infestation.h"
#include "hooks/orders/interceptor_return_order.h"
#include "hooks/orders/junkyarddog_order.h"
#include "hooks/orders/larva_creep_spawn.h"
#include "hooks/orders/larva_order.h"
#include "hooks/orders/liftland.h"
#include "hooks/orders/load_unload_orders.h"
#include "hooks/orders/medic_orders.h"
#include "hooks/orders/merge_units.h"
#include "hooks/orders/powerup.h"
#include "hooks/orders/repair_order.h"
#include "hooks/orders/rescuable_order.h"
#include "hooks/orders/research_upgrade_orders.h"
#include "hooks/orders/reset_collision.h"
#include "hooks/orders/shield_recharge_orders.h"
#include "hooks/orders/shrouded_order.h"
#include "hooks/orders/siege_transform.h"
#include "hooks/orders/spells/cast_order.h"
#include "hooks/orders/spells/defense_matrix.h"
#include "hooks/orders/spells/feedback_spell.h"
#include "hooks/orders/spells/hallucination_spell.h"
#include "hooks/orders/spells/mindcontrol_spell.h"
#include "hooks/orders/spells/nuke_orders.h"
#include "hooks/orders/spells/recall_spell.h"
#include "hooks/orders/spells/scanner_orders.h"
#include "hooks/orders/spidermine.h"
#include "hooks/orders/unit_making/unit_morph.h"
#include "hooks/orders/unit_making/unit_train.h"
#include "hooks/orders/warpin.h"
#include "hooks/recv_commands/CMDRECV_Build.h"
#include "hooks/recv_commands/CMDRECV_Cancel.h"
#include "hooks/recv_commands/CMDRECV_LiftOff.h"
#include "hooks/recv_commands/CMDRECV_MergeArchon.h"
#include "hooks/recv_commands/CMDRECV_Morph.h"
#include "hooks/recv_commands/CMDRECV_QueuedOrder.h"
#include "hooks/recv_commands/CMDRECV_ResearchUpgrade.h"
#include "hooks/recv_commands/CMDRECV_RightClick.h"
#include "hooks/recv_commands/CMDRECV_Selection.h"
#include "hooks/recv_commands/CMDRECV_SiegeTank.h"
#include "hooks/recv_commands/CMDRECV_Stimpack.h"
#include "hooks/recv_commands/CMDRECV_Stop.h"
#include "hooks/recv_commands/burrow_tech.h"
#include "hooks/recv_commands/receive_command.h"
#include "hooks/recv_commands/train_cmd_receive.h"
#include "hooks/right_click_CMDACT.h"
#include "hooks/right_click_returnedOrders.h"
#include "hooks/utils/CMDRECV_SaveLoadWrappers.h"
#include "hooks/utils/ExtendSightLimit.h"
#include "hooks/utils/replace_unit.h"
#include "hooks/utils/utils.h"
#include "hooks/weapons/weapon_impact.h"
#include "hooks/weapons/wpnspellhit.h"
#include "hooks/weapons/wpnsplash.h"

/// This function is called when the plugin is loaded into StarCraft.
/// You can enable/disable each group of hooks by commenting them.
/// You can also add custom modifications to StarCraft.exe by using:
///		memoryPatch(address_to_patch, value_to_patch_with);

BOOL WINAPI Plugin::InitializePlugin(IMPQDraftServer *lpMPQDraftServer) {

	// StarCraft.exe version check
	char exePath[300];
	const DWORD pathLen = GetModuleFileName(NULL, exePath, sizeof(exePath));

	if (pathLen == sizeof(exePath)) {
		MessageBox(NULL, "Error: Cannot check version of StarCraft.exe. The file path is too long.", NULL, MB_OK);
		return FALSE;
	}

	if (!checkStarCraftExeVersion(exePath))
		return FALSE;

	switch (MessageBox(NULL, "Do you want to use window mode?", "StarCraft: Manifold", MB_YESNOCANCEL)) {
		case IDYES:
			{
				char dll0Path[260]; // WMode.dll
				strncpy_s(dll0Path, sizeof(dll0Path), exePath, strrchr(exePath, '\\') - exePath + 1);
				strcat_s(dll0Path, sizeof(dll0Path), "WMode.dll");
				HANDLE cFile0 = CreateFile(dll0Path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING,
				                           FILE_ATTRIBUTE_NORMAL, NULL);
				if (cFile0 == (HANDLE)EOF) {
					FILE *fout0;
					fopen_s(&fout0, dll0Path, "wb");
					if (fout0 == NULL) {
						fprintf(stderr, "Memory allocation failed for fout\n");
						break;
					}
					fwrite(wModeData, sizeof(wModeData), 1, fout0);
					fclose(fout0);
				}
				CloseHandle(cFile0);

				char dll1Path[260]; // WMode_Fix.dll
				strncpy_s(dll1Path, sizeof(dll1Path), exePath, strrchr(exePath, '\\') - exePath + 1);
				strcat_s(dll1Path, sizeof(dll1Path), "WMode_Fix.dll");
				HANDLE cFile1 = CreateFile(dll1Path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING,
				                           FILE_ATTRIBUTE_NORMAL, NULL);
				if (cFile1 == (HANDLE)EOF) {
					FILE *fout1;
					fopen_s(&fout1, dll1Path, "wb");
					if (fout1 == NULL) {
						fprintf(stderr, "Memory allocation failed for fout\n");
						break;
					}
					fwrite(wModeFixData, sizeof(wModeFixData), 1, fout1);
					fclose(fout1);
				}
				CloseHandle(cFile1);

				HINSTANCE hDll;
				hDll = LoadLibrary(dll0Path);
				if (!hDll)
					MessageBox(NULL, "Failed inject WMODE.dll.", NULL, MB_ICONERROR);
				hDll = LoadLibrary(dll1Path);
				if (!hDll)
					MessageBox(NULL, "Failed inject WMODE_Fix.dll.", NULL, MB_ICONERROR);
			}
			break;
		case IDCANCEL:
			exit(NULL);
			return false;
	}

	hooks::injectGameHooks();
	hooks::injectDrawHook();

	// in order of creation
	hooks::injectInfestationHooks();
	hooks::injectSiegeTransformHooks();
	hooks::injectButtonSetHooks();
	hooks::injectSelectMod();
	hooks::injectMergeUnitsHooks();
	hooks::injectLarvaCreepSpawnHooks();
	hooks::injectLiftLandHooks();
	hooks::injectAttackOrdersHooks();
	hooks::injectStopHoldPosOrdersHooks();
	hooks::injectRecallSpellHooks();
	hooks::injectEnterNydusHooks();
	hooks::injectCastOrderHooks();
	hooks::injectWpnSpellHitHooks();
	hooks::injectBuildingMorphHooks();
	hooks::injectMakeNydusExitHook();
	hooks::injectUnitMorphHooks();
	hooks::injectWireframeHook();
	hooks::injectDieOrdersHook();
	hooks::injectBuildingTerranHook();
	hooks::injectBuildingProtossHooks();
	hooks::injectUnitTrainHooks();
	hooks::injectLoadUnloadProcHooks();
	hooks::injectLoadUnloadOrdersHooks();
	hooks::injectNukeOrdersHooks();
	hooks::injectBurrowTechHooks();
	hooks::injectResearchUpgradeOrdersHooks();
	hooks::injectMedicOrdersHooks();
	hooks::injectHallucinationSpellHook();
	hooks::injectFeedbackSpellHook();
	hooks::injectBtnsCondHook();
	hooks::injectRecvCmdHook();
	hooks::injectCreateInitUnitsHooks();
	hooks::injectGiveUnitHook();
	hooks::injectTrainCmdRecvHooks();
	hooks::injectCMDRECV_SiegeTankHooks();
	hooks::injectCMDRECV_MergeArchonHooks();
	hooks::injectCMDRECV_MorphHooks();
	hooks::injectCMDRECV_StopHooks();
	hooks::injectCMDRECV_CancelHooks();
	hooks::injectSelectLarvaHooks();
	hooks::injectRepairOrderHook();
	hooks::injectStatsPanelDisplayHook();
	hooks::injectUtilsHooks();
	hooks::injectMindControlSpellHook();
	hooks::injectCMDRECV_BuildHooks();
	hooks::injectExtendSightLimitMod();
	hooks::injectUpdateSelectedUnitsDataHook();
	hooks::injectStatsDisplayMainHook();
	hooks::injectUnitStatSelectionHooks();
	hooks::injectUnitStatCondHooks();
	hooks::injectUnitStatActHooks();
	hooks::injectStatusBaseTextHooks();
	hooks::injectStatusSupplyProviderHook();
	hooks::injectStatusResearchUpgradeHooks();
	hooks::injectStatusTransportHooks();
	hooks::injectStatusNukeSilo_Resources_Hooks();
	hooks::injectStatusBuildMorphTrain_Hooks();
	hooks::injectCMDRECV_SelectionHooks();
	hooks::injectCMDRECV_LiftOffHook();
	hooks::injectCMDRECV_ResearchUpgradeHooks();
	hooks::injectReplaceUnitWithTypeHook();
	hooks::injectCMDRECV_StimpackHook();
	hooks::injectCMDRECV_RightClickHook();
	hooks::injectRightClickCMDACT_Hooks();
	hooks::injectCMDRECV_QueuedOrderHooks();
	hooks::injectResetCollisionHooks();
	hooks::injectOrdersRootHooks();
	hooks::injectShroudedOrderHook();
	hooks::injectMoveOrdersHooks();
	hooks::injectShieldRechargeOrdersHooks();
	hooks::injectSpiderMineHooks();
	hooks::injectPowerupOrderHooks();
	hooks::injectInterceptorReturnOrderHook();
	hooks::injectLarvaOrderHook();
	hooks::injectHarvestOrdersHooks();
	hooks::injectBurrowOrdersHooks();
	hooks::injectCloakNearbyUnitsOrderHook();
	hooks::injectRightClickOrderHooks();
	hooks::injectScannerOrdersHook();
	hooks::injectDefensiveMatrixHooks();
	hooks::injectPatrolOrderHook();
	hooks::injectRescuableOrderHook();
	hooks::injectCritterOrderHook();
	hooks::injectDoodadOrdersHooks();
	hooks::injectWarpinOrderHook();
	hooks::injectJunkYardDogOrderHook();
	hooks::injectRightClickReturnedOrdersHooks();
	hooks::injectUnitPortraitHooks();
	hooks::injectWeaponImpactHook();
	hooks::injectWpnSplashHooks();
	hooks::injectAttackAndCooldownHook();
	hooks::injectCheatCodesHooks();
	hooks::injectCMDRECV_SaveLoadWrappersHooks();

	hooks::injectApplyUpgradeFlags();
	hooks::injectAttackPriorityHooks();
	hooks::injectBunkerHooks();
	hooks::injectCloakNearbyUnits();
	hooks::injectCloakingTechHooks();
	hooks::injectDetectorHooks();
	hooks::injectHarvestResource();
	hooks::injectRallyHooks();
	hooks::injectRechargeShieldsHooks();
	hooks::injectStimPacksHooks();
	hooks::injectTechTargetCheckHooks();
	hooks::injectTransferTechAndUpgradesHooks();
	hooks::injectUnitSpeedHooks();
	hooks::injectUpdateStatusEffects();
	hooks::injectUpdateUnitState();
	hooks::injectWeaponCooldownHook();
	hooks::injectWeaponDamageHook();
	hooks::injectWeaponFireHooks();

	hooks::injectUnitDestructorSpecial();
	hooks::injectPsiFieldHooks();

	hooks::injectArmorBonusHook();
	hooks::injectSightRangeHook();
	hooks::injectUnitMaxEnergyHook();
	hooks::injectWeaponRangeHooks();

	hooks::injectUnitTooltipHook();

	// fix to make sc1 campaign playable from firegraft/mpqgraft self-executables
	jmpPatch((void *)0x15017960,
	         0x004100C4); // insert a "jump to storm.dll function" instead of a "jump to firegraft function" in an array

	return TRUE;
}
