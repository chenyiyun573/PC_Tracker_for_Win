#!/usr/bin/env python3

import os
import sys

# You can customize these lists to your needs:
IGNORED_DIRS = {
    '.git',
    'node_modules',
    '.venv',
    'venv',
    'dist',
    'build',
    '__pycache__',
    '.idea',
    '.vs',
}

# Common binary or otherwise unneeded file types
IGNORED_EXTENSIONS = {
    '.exe',
    '.dll',
    '.pyc',
    '.pyo',
    '.so',
    '.dylib',
    '.zip',
    '.tar',
    '.gz',
    '.jpg',
    '.jpeg',
    '.png',
    '.gif',
    '.ico',
    '.pdf',
    '.exe',
    '.DS_Store',
}

# Code or script extensions for which we want to include a code block
CODE_EXTENSIONS = {
    '.py': 'python',
    '.js': 'javascript',
    '.ts': 'typescript',
    '.jsx': 'javascript',
    '.tsx': 'typescript',
    '.sh': 'bash',
    '.bat': 'bat',
    '.yml': 'yaml',
    '.yaml': 'yaml',
    '.json': 'json',
    '.html': 'html',
    '.css': 'css',
    '.scss': 'scss',
    '.c': 'c',
    '.cpp': 'cpp',
    '.h': 'c',
    '.hpp': 'cpp',
    '.java': 'java',
    '.md': 'markdown',
    # Add more as needed
}

def get_tree_structure(root_path):
    """
    Build a text-based tree structure of files/folders,
    ignoring the directories listed in IGNORED_DIRS and 
    ignoring files with IGNORED_EXTENSIONS.
    """
    tree_lines = []
    
    def _walk(directory, prefix=''):
        # Gather subdirectories and files
        entries = sorted(os.listdir(directory))
        entries_filtered = [
            e for e in entries
            if not e.startswith('.')  # skip hidden items (like .DS_Store) 
               or e in ('.gitignore',)  # you can allow .gitignore specifically if you want
        ]
        
        for i, entry in enumerate(entries_filtered):
            full_path = os.path.join(directory, entry)
            
            # Determine tree branch style
            connector = 'â””â”€â”€ ' if i == len(entries_filtered) - 1 else 'â”œâ”€â”€ '
            
            # If it's a directory, check if we should ignore it
            if os.path.isdir(full_path):
                if entry in IGNORED_DIRS:
                    continue
                tree_lines.append(prefix + connector + entry + '/')
                
                # Build the next level prefix
                extension_prefix = '    ' if i == len(entries_filtered) - 1 else 'â”‚   '
                _walk(full_path, prefix + extension_prefix)
            else:
                # If it's a file, check if it's an ignored file extension
                file_ext = os.path.splitext(entry)[1]
                if file_ext.lower() in IGNORED_EXTENSIONS:
                    continue
                
                tree_lines.append(prefix + connector + entry)

    _walk(root_path)
    return "\n".join(tree_lines)


def generate_markdown_summary(root_path, output_md='folder_summary.md'):
    """
    Generate a Markdown file summarizing the project structure and code contents.
    """
    
    # 1. Get the tree structure
    tree_text = get_tree_structure(root_path)
    
    # 2. Start building Markdown content
    markdown_lines = []
    markdown_lines.append(f"# Folder Summary for `{os.path.basename(root_path)}`\n")
    markdown_lines.append("## Tree Structure\n")
    markdown_lines.append("```")
    markdown_lines.append(tree_text)
    markdown_lines.append("```")
    markdown_lines.append("")
    
    # 3. Walk the directory and include code blocks for recognized files
    for current_dir, dirs, files in os.walk(root_path):
        # Filter out ignored directories in-place
        dirs[:] = [d for d in dirs if d not in IGNORED_DIRS]

        for file_name in files:
            file_ext = os.path.splitext(file_name)[1].lower()
            
            if file_ext in IGNORED_EXTENSIONS:
                continue  # skip ignored file types
            
            # If it's a recognized code extension, read it and add to markdown
            if file_ext in CODE_EXTENSIONS:
                # figure out the code language
                lang = CODE_EXTENSIONS[file_ext]
                
                file_path = os.path.join(current_dir, file_name)
                relative_path = os.path.relpath(file_path, root_path)
                
                try:
                    with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
                        content = f.read()
                    
                    markdown_lines.append(f"## `{relative_path}`")
                    markdown_lines.append(f"```{lang}")
                    markdown_lines.append(content)
                    markdown_lines.append("```")
                    markdown_lines.append("")  # extra line for spacing
                except Exception as e:
                    print(f"Could not read file {file_path}: {e}")
    
    # 4. Write the result to a Markdown file
    with open(output_md, 'w', encoding='utf-8') as md_file:
        md_file.write("\n".join(markdown_lines))
    
    print(f"Markdown summary generated at: {output_md}")


def main():
    # If a directory was passed in CLI arguments, use it; else ask the user.
    if len(sys.argv) > 1:
        target_dir = sys.argv[1]
    else:
        target_dir = input("Enter the path to the folder you want to summarize: ").strip()
    
    if not os.path.isdir(target_dir):
        print(f"Error: '{target_dir}' is not a valid directory.")
        sys.exit(1)

    # Generate the summary
    generate_markdown_summary(target_dir)

if __name__ == "__main__":
    main()