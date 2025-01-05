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


