﻿using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using KSP.UI.Screens;
using System;
using MonthlyBudgets_KACWrapper;

namespace MonthlyBudgets
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, true)]
    public class MonthlyBudgets : MonoBehaviour
    {
        public static MonthlyBudgets instance;
        public double lastUpdate = 0;
        public float emergencyBudgetPercentage = 10;
        public bool enableEmergencyBudget;
        public double emergencyBudget = 0;
        bool showGUI = false;
        ApplicationLauncherButton toolbarButton;
        Rect Window = new Rect(20, 100, 240, 50);
        bool timeDiscrepancyLog = true;
        CelestialBody HomeWorld;
        double dayLength;
        double yearLength;
        public string inputString;
        public float researchBudget = 0;
        public bool jokeSeen = false;
        private SpaceCenterFacility[] facilities;
        public int launchCosts = 0;


        private void Budget(double timeSinceLastUpdate)
        {
            try
            {
                double funds = Funding.Instance.Funds;
                float costs = 0;
                double offsetFunds = funds;
                if ((BudgetSettings.instance.friendlyInterval*dayLength) * 2 > timeSinceLastUpdate)
                {
                    if (BudgetSettings.instance.hardMode)
                    {
                        int penalty = (int)funds / 10000;
                        if (penalty < Reputation.CurrentRep) Reputation.Instance.AddReputation(-penalty, TransactionReasons.None);
                        else Reputation.Instance.AddReputation(-Reputation.CurrentRep, TransactionReasons.None);
                        Debug.Log("[MonthlyBudgets]: " + funds + "remaining, " + penalty + " reputation removed");
                    }
                    costs = CostCalculate(true);
                    if (BudgetSettings.instance.launchCostsEnabled)
                    {
                        costs += launchCosts;
                        launchCosts = 0;
                    }
                    offsetFunds = funds - costs;
                    if (offsetFunds < 0) offsetFunds = 0;
                }
                float rep = Reputation.CurrentRep;
                double budget = (rep * BudgetSettings.instance.multiplier) - costs;
                if(researchBudget >0)
                {
                    float rnd = ((float)budget/10000) * (researchBudget / 100);
                    ResearchAndDevelopment.Instance.AddScience(rnd, TransactionReasons.RnDs);
                    ScreenMessages.PostScreenMessage("R&D Department have provided " + Math.Round(rnd,1) + " science this month");
                    Debug.Log("[MonthlyBudgets]: " + Math.Round(rnd,1) + " science awarded by R&D");
                    Reputation.Instance.AddReputation(-(Reputation.CurrentRep * (researchBudget / 100)), TransactionReasons.RnDs);
                    budget = budget - (budget * (researchBudget / 100));
                }
                //we shouldn't take money away. If the player holds more than the budget, just don't award.
                if (budget <= offsetFunds)
                {
                    ScreenMessages.PostScreenMessage("We can't justify extending your budget this month");
                    if (budget < costs || !BudgetSettings.instance.coverCosts)
                    {
                        if (costs > 0)
                        {
                            Funding.Instance.AddFunds(-costs, TransactionReasons.None);
                            ScreenMessages.PostScreenMessage("This month's costs total " + costs.ToString("C"));
                        }
                    }
                    else
                    {
                        ScreenMessages.PostScreenMessage("The budget will cover your costs");
                        float repLoss = costs / BudgetSettings.instance.multiplier;
                        Reputation.Instance.AddReputation(-repLoss, TransactionReasons.None);
                    }
                    Debug.Log("[MonthlyBudgets]: Budget of " + budget + " is less than available funds of " + funds);
                }
                else
                {
                    if (enableEmergencyBudget)
                    {
                        double upgradeBudgetReserved = budget * (emergencyBudgetPercentage / 100);
                        budget = budget - upgradeBudgetReserved;
                        emergencyBudget = emergencyBudget + upgradeBudgetReserved;
                        emergencyBudget = Math.Round(emergencyBudget, 0);
                        Debug.Log("[MonthlyBudgets]: Diverted " + emergencyBudgetPercentage + "% of budget. BPF is now: "+emergencyBudget);
                    }
                    Funding.Instance.AddFunds(-funds, TransactionReasons.None);
                    Funding.Instance.AddFunds(budget, TransactionReasons.None);
                    ScreenMessages.PostScreenMessage("This month's budget is " + budget.ToString("C"));
                    Debug.Log("[MonthlyBudgets]: Budget awarded: " + budget);
                }
                lastUpdate = lastUpdate + (BudgetSettings.instance.friendlyInterval*dayLength);
                if (BudgetSettings.instance.decayEnabled)
                {
                    Reputation.Instance.AddReputation(-Reputation.CurrentRep*(BudgetSettings.instance.repDecay/100.0f), TransactionReasons.None);
                    Debug.Log("[MonthlyBudgets]: Removing " + BudgetSettings.instance.repDecay / 100 + "% Reputation");
                }
                if(!KACWrapper.AssemblyExists && BudgetSettings.instance.stopTimewarp)
                {
                    TimeWarp.SetRate(0, true);
                }
            }
            catch
            {
                Debug.Log("[MonthlyBudgets]: Problem calculating the budget");
            }
        }

        void Awake()
        {
            DontDestroyOnLoad(this);
            if (!BudgetSettings.instance.masterSwitch) Destroy(this);
            instance = this;
            GameEvents.onGUIApplicationLauncherReady.Add(GUIReady);
            GameEvents.onGameSceneSwitchRequested.Add(onGameSceneSwitchRequested);

        }
        void Start()
        {
            KACWrapper.InitKACWrapper();
            PopulateHomeWorldData();
            facilities = (SpaceCenterFacility[])Enum.GetValues(typeof(SpaceCenterFacility));
            GameEvents.OnVesselRollout.Add(OnVesselRollout);
        }

        private void OnVesselRollout(ShipConstruct ship)
        {
            if (!BudgetSettings.instance.launchCostsEnabled) return;
            if (ship.shipFacility == EditorFacility.VAB) launchCosts += BudgetSettings.instance.launchCostsVAB;
            else launchCosts += BudgetSettings.instance.launchCostsSPH;
        }

        void Update()
        {
            if (HighLogic.CurrentGame == null) return;
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER) return;
            if (lastUpdate == 99999) return;
            if (emergencyBudgetPercentage < 1) emergencyBudgetPercentage = 10;
            if (emergencyBudgetPercentage > 50) emergencyBudgetPercentage = 50;
            double time = (Planetarium.GetUniversalTime());
            while (lastUpdate > time)
            {
                lastUpdate = lastUpdate - (BudgetSettings.instance.friendlyInterval * dayLength);
                if (timeDiscrepancyLog)
                {
                    Debug.Log("[MonthlyBudgets]: Last update was in the future. Using time machine to correct");
                    timeDiscrepancyLog = false;
                }
            }
            double timeSinceLastUpdate = time - lastUpdate;
            if (timeSinceLastUpdate >= (BudgetSettings.instance.friendlyInterval * dayLength))
            {
                Budget(timeSinceLastUpdate);
            }
            if (KACWrapper.AssemblyExists && BudgetSettings.instance.stopTimewarp)
            {
                if (!KACWrapper.APIReady) return;
                KACWrapper.KACAPI.KACAlarmList alarms = KACWrapper.KAC.Alarms;
                if (alarms.Count == 0) return;
                for (int i = 0; i < alarms.Count; i++)
                {
                    string s = alarms[i].Name;
                    if (s == "Next Budget") return;
                }
                KACWrapper.KAC.CreateAlarm(KACWrapper.KACAPI.AlarmTypeEnum.Raw, "Next Budget", lastUpdate + (BudgetSettings.instance.friendlyInterval * dayLength));
            }
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(GUIReady);
            GameEvents.onGameSceneSwitchRequested.Remove(onGameSceneSwitchRequested);
        }

        private int CostCalculate(bool log)
        {
            IEnumerable<ProtoCrewMember> crew = HighLogic.CurrentGame.CrewRoster.Crew;
            int budget = 0;
            foreach (ProtoCrewMember p in crew)
            {
                if (p.type == ProtoCrewMember.KerbalType.Tourist) continue;
                float level = p.experienceLevel;
                if (level == 0) level = 0.5f;
                float wages = 0;
                if (p.rosterStatus == ProtoCrewMember.RosterStatus.Available) wages = level * BudgetSettings.instance.availableWages;
                if (p.rosterStatus == ProtoCrewMember.RosterStatus.Assigned) wages = level * BudgetSettings.instance.assignedWages;
                budget += (int)wages;
            }
            IEnumerable<Vessel> vessels = FlightGlobals.Vessels.Where(v => v.vesselType != VesselType.Debris && v.vesselType != VesselType.Flag && v.vesselType != VesselType.SpaceObject && v.vesselType != VesselType.Unknown && v.vesselType != VesselType.EVA);
            budget += vessels.Count() * BudgetSettings.instance.vesselCost;
            if(BudgetSettings.instance.buildingCostsEnabled)
            {
                for(int i = 0; i<facilities.Count(); i++)
                {
                    SpaceCenterFacility facility = facilities.ElementAt(i);
                    if (facility == SpaceCenterFacility.LaunchPad || facility == SpaceCenterFacility.Runway) continue;
                    int lvl = (int)Math.Round(ScenarioUpgradeableFacilities.GetFacilityLevel(facility) * ScenarioUpgradeableFacilities.GetFacilityLevelCount(facility)) + 1;
                    budget += lvl * BudgetSettings.instance.buildingCosts;
                }
                if (HighLogic.CurrentGame.Parameters.Difficulty.AllowOtherLaunchSites) budget += (2 * BudgetSettings.instance.buildingCosts);
            }
            if (log)
            {
                Debug.Log("[MonthlyBudgets]: Expenses are " + budget);
            }
            return budget;
        }
      

        public void OnGUI()
        {
            if (showGUI)
            {
               Window = GUILayout.Window(65468754, Window, GUIDisplay, "MonthlyBudgets", GUILayout.Width(200));
            }
        }
        public void GUIReady()
        {
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER || HighLogic.LoadedScene == GameScenes.MAINMENU) return;
            if (toolbarButton == null)
            {
                toolbarButton = ApplicationLauncher.Instance.AddModApplication(GUISwitch, GUISwitch, null, null, null, null, ApplicationLauncher.AppScenes.ALWAYS, GameDatabase.Instance.GetTexture("MonthlyBudgets/Icon", false));
            }
        }

        void PopulateHomeWorldData()
        {
            HomeWorld = FlightGlobals.GetHomeBody();
            dayLength = HomeWorld.solarDayLength;
            yearLength = HomeWorld.orbit.period;
        }

        void GUIDisplay(int windowID)
        {
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
            {
                GUILayout.Label("MonthlyBudgets is only available in Career Games");
                return;
            }
            if (HomeWorld == null) PopulateHomeWorldData();
            int costs = CostCalculate(false);
            double estimatedBudget = Math.Round(Reputation.CurrentRep * BudgetSettings.instance.multiplier,0);
            if (estimatedBudget < 0)
            {
                estimatedBudget = 0;
            }
            double nextUpdateRaw = lastUpdate + (BudgetSettings.instance.friendlyInterval * dayLength);
            double nextUpdateRefine = nextUpdateRaw / dayLength;
            int year = 1;
            int day = 1;
            while (nextUpdateRefine > yearLength / dayLength)
            {
                year = year + 1;
                nextUpdateRefine = nextUpdateRefine - (yearLength / dayLength);
            }
            day = day + (int)nextUpdateRefine;
            GUILayout.Label("Next Budget Due: Y " + year + " D " + day);
            GUILayout.Label("Estimated Budget: $" + estimatedBudget);
            GUILayout.Label("Current Costs: $" + costs);
            if (BudgetSettings.instance.launchCostsEnabled) GUILayout.Label("Launch Costs: " + launchCosts);
            GUILayout.Label("Percentage of budget dedicated to R&D");
            if (!float.TryParse(GUILayout.TextField(researchBudget.ToString()), out researchBudget) || researchBudget <0 || researchBudget >100) researchBudget = 0;
            enableEmergencyBudget = GUILayout.Toggle(enableEmergencyBudget, "Enable Big Project Fund");
            GUILayout.Label("Big Project Fund: $" + emergencyBudget);
            GUILayout.Label("Percentage to divert to fund");
            float.TryParse(GUILayout.TextField(emergencyBudgetPercentage.ToString()), out emergencyBudgetPercentage);
            if (GUILayout.Button("Withdraw Funds from Big Project Fund"))
            {
                Funding.Instance.AddFunds(emergencyBudget, TransactionReasons.Strategies);
                emergencyBudget = 0;
                enableEmergencyBudget = false;
            }
            if (GUILayout.Button("Settings")) BudgetSettings.instance.showGUI = true;
            GUI.DragWindow();
        }

        public void GUISwitch()
        {
            showGUI = !showGUI;
        }

        void onGameSceneSwitchRequested(GameEvents.FromToAction<GameScenes, GameScenes> data)
        {
            if (toolbarButton == null) return;
            ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
            showGUI = false;
        }
    }
}