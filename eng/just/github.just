set working-directory := "../.."

# Run the tests of .github/scripts: the title, commit and branch conventions, and which checks a change affects
conventions:
    node --test .github/scripts/*.test.mjs
