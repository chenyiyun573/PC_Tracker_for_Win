This repo is heavily based on the repo of the paper PC Agent.
I am using this repo only for its PC tracker.exe now. 

### Setup

To get started with PC Agent, we recommend setting up your Python environment using conda:

```bash
# Clone the repository and navigate to the folder
git clone https://github.com/GAIR-NLP/PC-Agent.git
cd PC-Agent
# Create and activate conda environment
conda env create -f environment.yml
conda activate pcagent
```

### PC Tracker

PC Tracker is an infrastructure for human-computer interaction data collection. The source code in `tracker/` directory can be modified to fit your specific data collection requirements.

To deploy:
1. Build the executable (Windows):
```powershell
cd tracker
.\package.ps1
```
2. Customize `tasks.json` according to your annotation needs
3. Distribute to annotators
4. Collect annotation data from annotators - annotated data will be saved in the `events/` folder (hidden) under working directory


#### 20250105 1325 PT

Based on the original code, I mainly added lines into the monitor.py to prevent multiple press events recorded when press like Ctrl + A format hotkey. Without these 
```
#Yuantsy Modification
# if key is already in pressed set, ignore repeated press
if key in self.currently_pressed_keys:
    return 
# Otherwise, mark it as pressed
self.currently_pressed_keys.add(key)
#Yuantsy Modifcation End
```
The Ctrl + A will bring us mutiple press ctrl events which is unnecessary. 

