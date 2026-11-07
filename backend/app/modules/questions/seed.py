"""Seed question bank.

These are *seed* questions: the interviewer model picks from them, rephrases naturally and
asks adaptive follow-ups based on the candidate's answers. Role-agnostic behavioral and HR
questions use `role_id="*"`.
"""

from __future__ import annotations

ANY_ROLE = "*"

_TECHNICAL: dict[str, dict[str, list[str]]] = {
    "software_engineer": {
        "beginner": [
            "Explain the difference between an array and a linked list, and when you would use each.",
            "What is the time complexity of searching in a hash map, and what can make it worse?",
            "How do you approach debugging a function that returns the wrong result?",
        ],
        "intermediate": [
            "How would you detect a cycle in a linked list? Walk me through the complexity.",
            "Describe how you would design a URL shortener's data model and API.",
            "What makes code easy to review? Give an example of feedback you'd leave on a pull request.",
        ],
        "advanced": [
            "Design a rate limiter for a distributed API. What trade-offs do the common algorithms have?",
            "How would you find and fix a memory leak in a long-running service?",
            "Walk me through designing a news feed for millions of users, including caching and fan-out.",
        ],
    },
    "ai_ml_engineer": {
        "beginner": [
            "What is overfitting and how can you reduce it?",
            "Explain the difference between supervised and unsupervised learning with examples.",
            "How do you split data for training, validation and testing, and why?",
        ],
        "intermediate": [
            "How would you evaluate a retrieval-augmented generation system?",
            "Explain the bias-variance trade-off and how it shows up in practice.",
            "How would you deploy a model and monitor it for data drift?",
        ],
        "advanced": [
            "Design an evaluation pipeline for an LLM application with safety and quality metrics.",
            "How would you reduce inference latency and cost for a large transformer model in production?",
            "Discuss fine-tuning versus prompting versus retrieval for a domain-specific assistant.",
        ],
    },
    "data_scientist": {
        "beginner": [
            "What is the difference between mean and median, and when is each more appropriate?",
            "How would you handle missing values in a dataset?",
            "Explain what a p-value tells you and what it does not.",
        ],
        "intermediate": [
            "How would you design an A/B test for a new checkout button? What could go wrong?",
            "Write, in words, a SQL query to find each customer's most recent order.",
            "How do you choose evaluation metrics for an imbalanced classification problem?",
        ],
        "advanced": [
            "How would you measure the causal impact of a feature when you can't run an experiment?",
            "Explain how you would build and validate a churn prediction model end to end.",
            "How do you communicate an uncertain result to non-technical stakeholders?",
        ],
    },
    "frontend_developer": {
        "beginner": [
            "What is the difference between let, const and var in JavaScript?",
            "How does the CSS box model work?",
            "What makes a web page accessible to screen-reader users?",
        ],
        "intermediate": [
            "Explain how a component re-renders in a framework like React and how to avoid unnecessary renders.",
            "How would you improve the loading performance of a slow single-page application?",
            "How do you manage state that is shared across many components?",
        ],
        "advanced": [
            "Design the architecture of a large frontend app used by several teams.",
            "How would you implement and test an accessible, keyboard-navigable autocomplete?",
            "Explain the trade-offs between client-side rendering, server-side rendering and static generation.",
        ],
    },
    "backend_developer": {
        "beginner": [
            "What is the difference between SQL and NoSQL databases?",
            "Explain what a REST API is and what HTTP status codes you'd use for common errors.",
            "What is an index in a database and why does it help?",
        ],
        "intermediate": [
            "How would you design authentication for a mobile app's API?",
            "Explain how you would handle a slow database query in production.",
            "How do you make an API endpoint idempotent, and why does it matter?",
        ],
        "advanced": [
            "Design a job queue that guarantees each job is processed at least once.",
            "How would you scale a write-heavy service that is hitting database limits?",
            "Walk me through how you'd investigate intermittent 500 errors across microservices.",
        ],
    },
}

_BEHAVIORAL = [
    "Tell me about a time you disagreed with a teammate. How did you resolve it?",
    "Describe a project that didn't go as planned. What did you learn?",
    "Tell me about a time you had to learn something new quickly.",
    "Give an example of when you took ownership of a problem outside your responsibilities.",
    "Describe a situation where you had to balance several deadlines.",
    "Tell me about feedback you received that changed how you work.",
    "Describe a time you explained a complex idea to a non-technical person.",
    "Tell me about an accomplishment you are particularly proud of.",
]

_HR = [
    "Tell me about yourself and what brings you to this role.",
    "Why are you interested in this company and position?",
    "What are you looking for in your next team and manager?",
    "Where do you see yourself growing over the next two to three years?",
    "What kind of work environment helps you do your best work?",
    "How do you handle stress or pressure at work?",
    "What are your strengths, and what are you actively working to improve?",
    "Do you have any questions for us about the role or the team?",
]


def build_seed_documents() -> list[dict]:
    docs: list[dict] = []
    n = 0
    for role_id, by_level in _TECHNICAL.items():
        for difficulty, questions in by_level.items():
            for text in questions:
                n += 1
                docs.append({"_id": f"q{n:04d}", "role_id": role_id, "interview_type": "technical",
                             "difficulty": difficulty, "text": text})
    for text in _BEHAVIORAL:
        n += 1
        docs.append({"_id": f"q{n:04d}", "role_id": ANY_ROLE, "interview_type": "behavioral",
                     "difficulty": "*", "text": text})
    for text in _HR:
        n += 1
        docs.append({"_id": f"q{n:04d}", "role_id": ANY_ROLE, "interview_type": "hr",
                     "difficulty": "*", "text": text})
    return docs
