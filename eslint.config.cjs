// SPDX-License-Identifier: EUPL-1.2
const js = require('@eslint/js');
const globals = require('globals');

module.exports = [
    {
        ignores: [
            '.claude/**',
            '.Codex/**',
            '.analysis-*/**',
            '.pipeline-artifacts/**',
            '.qa-test-results/**',
            'coverage*/**',
            '**/bin/**',
            '**/obj/**',
            '**/node_modules/**',
            'site/**',
            'src/Aetheus.Front/wwwroot/lib/**'
        ]
    },
    js.configs.recommended,
    {
        files: ['src/Aetheus.Front/wwwroot/js/**/*.js'],
        languageOptions: {
            ecmaVersion: 'latest',
            sourceType: 'script',
            globals: {
                ...globals.browser,
                Aetheus: 'writable',
                dagre: 'readonly',
                monaco: 'readonly',
                require: 'readonly'
            }
        },
        rules: {
            complexity: ['warn', 10],
            'no-eval': 'error',
            'no-implied-eval': 'error',
            'no-unused-vars': ['error', { argsIgnorePattern: '^_' }]
        }
    },
    {
        files: ['src/Aetheus.Front/wwwroot/js/dockerGraph.js'],
        languageOptions: { sourceType: 'module' }
    },
    {
        files: [
            'deploy/scripts/**/*.mjs',
            'packages/aetheus-web-analytics/test/**/*.js'
        ],
        languageOptions: {
            sourceType: 'module',
            globals: {
                ...globals.node,
                ...globals.browser
            }
        }
    },
    {
        files: ['packages/aetheus-web-analytics/src/**/*.js'],
        languageOptions: {
            sourceType: 'module',
            globals: globals.browser
        }
    },
    {
        files: ['tests/js/**/*.cjs'],
        languageOptions: {
            sourceType: 'commonjs',
            globals: {
                ...globals.node,
                ...globals.browser,
                Aetheus: 'writable'
            }
        }
    }
];
